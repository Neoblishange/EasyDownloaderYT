using EasyDownloaderYT.Services;
using EasyDownloaderYT.ViewModels;
using Xunit;

namespace EasyDownloaderYT.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void AddDownload_RejectsNonHttpUrl()
    {
        using var viewModel = CreateViewModel(new FakeDownloadService());

        Assert.False(viewModel.AddDownload("file:///C:/video.mp4", DownloadFormat.VideoMp4));
        Assert.Empty(viewModel.DownloadQueue);
    }

    [Fact]
    public async Task AddDownload_ValidUrlCompletesQueueItem()
    {
        var service = new FakeDownloadService();
        using var viewModel = CreateViewModel(service);

        Assert.True(viewModel.AddDownload(" https://example.invalid/video ", DownloadFormat.VideoMp4));

        DownloadQueueItemViewModel item = Assert.Single(viewModel.DownloadQueue);
        await WaitUntilAsync(() => item.State == DownloadQueueItemState.Completed);

        Assert.Equal("Test title", item.Title);
        Assert.Equal(1, service.DownloadCount);
    }

    [Fact]
    public async Task RetryAsync_RunsFailedItemAgain()
    {
        var service = new FakeDownloadService(failFirstDownload: true);
        using var viewModel = CreateViewModel(service);

        Assert.True(viewModel.AddDownload("https://example.invalid/video", DownloadFormat.AudioMp3));
        DownloadQueueItemViewModel item = Assert.Single(viewModel.DownloadQueue);
        await WaitUntilAsync(() => item.State == DownloadQueueItemState.Failed);

        await viewModel.RetryAsync(item);

        Assert.Equal(DownloadQueueItemState.Completed, item.State);
        Assert.Equal(2, service.DownloadCount);
    }

    [Fact]
    public async Task AddDownload_WavFormatIsPreservedThroughQueue()
    {
        var service = new FakeDownloadService();
        using var viewModel = CreateViewModel(service);

        Assert.True(viewModel.AddDownload("https://example.invalid/video", DownloadFormat.AudioWav));

        DownloadQueueItemViewModel item = Assert.Single(viewModel.DownloadQueue);
        await WaitUntilAsync(() => item.State == DownloadQueueItemState.Completed);

        Assert.Equal(DownloadFormat.AudioWav, item.Format);
        Assert.Equal("Format.AudioWav", item.FormatText);
        Assert.Equal(DownloadFormat.AudioWav, Assert.Single(service.DownloadFormats));
    }

    [Fact]
    public async Task Cancel_DuringTitleValidationRemovesItemAndCancelsLookup()
    {
        var service = new FakeDownloadService(blockTitleLookup: true);
        using var viewModel = CreateViewModel(service);
        viewModel.AddDownload("https://example.invalid/video", DownloadFormat.VideoMp4);
        DownloadQueueItemViewModel item = Assert.Single(viewModel.DownloadQueue);

        viewModel.Cancel(item);
        await WaitUntilAsync(() => service.TitleLookupCancelled);

        Assert.Empty(viewModel.DownloadQueue);
    }

    [Fact]
    public async Task Queue_ProcessesDownloadsSequentially()
    {
        var service = new FakeDownloadService(downloadDelay: TimeSpan.FromMilliseconds(20));
        using var viewModel = CreateViewModel(service);

        viewModel.AddDownload("https://example.invalid/first", DownloadFormat.VideoMp4);
        viewModel.AddDownload("https://example.invalid/second", DownloadFormat.VideoMp4);
        await WaitUntilAsync(() => viewModel.DownloadQueue.All(
            item => item.State == DownloadQueueItemState.Completed));

        Assert.Equal(1, service.MaximumConcurrentDownloads);
        Assert.Equal(
            new[] { "https://example.invalid/first", "https://example.invalid/second" },
            service.DownloadUrls);
    }

    private static MainViewModel CreateViewModel(FakeDownloadService service) =>
        new(service, key => key, _ => "test error", TimeSpan.Zero);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }
}

internal sealed class FakeDownloadService(
    bool failFirstDownload = false,
    bool blockTitleLookup = false,
    TimeSpan? downloadDelay = null) : IDownloadService
{
    private readonly bool _failFirstDownload = failFirstDownload;
    private readonly TaskCompletionSource<string> _titleLookup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _downloadDelay = downloadDelay ?? TimeSpan.Zero;
    private int _activeDownloads;
    internal int DownloadCount { get; private set; }
    internal bool TitleLookupCancelled { get; private set; }
    internal int MaximumConcurrentDownloads { get; private set; }
    internal List<string> DownloadUrls { get; } = new();
    internal List<DownloadFormat> DownloadFormats { get; } = new();

    public async Task<string> GetTitleAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!blockTitleLookup)
            return "Test title";

        try
        {
            return await _titleLookup.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TitleLookupCancelled = true;
            throw;
        }
    }

    public async Task DownloadAsync(
        string url,
        DownloadFormat format,
        IProgress<int> progress,
        IProgress<DownloadStage> stage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DownloadCount++;
        DownloadUrls.Add(url);
        DownloadFormats.Add(format);
        int active = Interlocked.Increment(ref _activeDownloads);
        MaximumConcurrentDownloads = Math.Max(MaximumConcurrentDownloads, active);
        try
        {
            if (_downloadDelay > TimeSpan.Zero)
                await Task.Delay(_downloadDelay, cancellationToken);

            if (_failFirstDownload && DownloadCount == 1)
                throw new DownloadException("Error.Failed", "synthetic failure");

            progress.Report(100);
            stage.Report(DownloadStage.Done);
        }
        finally
        {
            Interlocked.Decrement(ref _activeDownloads);
        }
    }

    public Task<UpdateResult> UpdateYtDlpAsync() => Task.FromResult(UpdateResult.UpToDate);
}
