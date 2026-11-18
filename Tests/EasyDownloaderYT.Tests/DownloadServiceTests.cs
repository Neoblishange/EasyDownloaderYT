using System.Diagnostics;
using EasyDownloaderYT.Services;
using Xunit;

namespace EasyDownloaderYT.Tests;

public sealed class DownloadServiceTests
{
    [Theory]
    [InlineData("ERROR: Unsupported URL: https://example.invalid", "Error.InvalidUrl")]
    [InlineData("ERROR: Invalid video ID: abc123", "Error.InvalidVideoId")]
    [InlineData("ERROR: Video ID does not exist", "Error.InvalidVideoId")]
    [InlineData("ERROR: Incomplete YouTube ID", "Error.InvalidVideoId")]
    [InlineData("ERROR: This video id is invalid", "Error.InvalidVideoId")]
    [InlineData("ERROR: This video is age-restricted", "Error.AgeRestricted")]
    [InlineData("ERROR: Private video", "Error.Private")]
    [InlineData("ERROR: This video is for members only", "Error.MembersOnly")]
    [InlineData("ERROR: This video is not available in your country", "Error.GeoBlocked")]
    [InlineData("ERROR: This live event will begin shortly", "Error.NotStarted")]
    [InlineData("ERROR: The video is currently live", "Error.Live")]
    [InlineData("ERROR: Requested format is not available", "Error.FormatUnavailable")]
    [InlineData("ERROR: Video unavailable", "Error.Unavailable")]
    [InlineData("ERROR: HTTP Error 429: Too Many Requests", "Error.RateLimit")]
    [InlineData("ERROR: Unable to download webpage: timed out", "Error.Network")]
    [InlineData("ERROR: Postprocessing: ffmpeg not found", "Error.Ffmpeg")]
    [InlineData("ERROR: Sign in to confirm you have access to this video", "Error.LoginRequired")]
    [InlineData("unexpected error", "Error.Failed")]
    [InlineData(null, "Error.Unknown")]
    [InlineData("", "Error.Unknown")]
    public void MapErrorKey_MatchesExpectedRule(string? error, string expectedResource)
    {
        Assert.Equal(expectedResource, DownloadService.MapErrorKey(error));
    }

    [Theory]
    [InlineData("[download] 37.8% of 10MiB", 37)]
    [InlineData("[download] 100% of 10MiB", 100)]
    [InlineData("[download] 100.0% of 10MiB", 100)]
    [InlineData("no progress reported", null)]
    public void ParseProgress_ReturnsIntegerPercentage(string line, int? expected)
    {
        Assert.Equal(expected, DownloadOutputParser.ParseProgress(line));
    }

    [Fact]
    public void GetOutputPath_RejectsPathsOutsideDownloadDirectory()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "outside.mp4");

        Assert.Null(DownloadOutputParser.GetOutputPath($"[download] Destination: {outside}", folder));
    }

    [Fact]
    public async Task GetTitleAsync_UsesRunnerAndReturnsTrimmedTitle()
    {
        using var temp = new TemporaryDirectory();
        string tools = Directory.CreateDirectory(Path.Combine(temp.Path, "tools")).FullName;
        File.WriteAllText(Path.Combine(tools, "yt-dlp.exe"), string.Empty);
        var runner = new FakeProcessRunner(new ProcessResult(0, "  A title  \r\n", string.Empty));
        var service = new DownloadService(runner, tools, Path.Combine(temp.Path, "downloads"));

        string title = await service.GetTitleAsync("https://example.invalid/video");

        Assert.Equal("A title", title);
        Assert.Contains("--get-title", runner.StartInfos[0].ArgumentList);
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgressAndCompletion()
    {
        using var temp = new TemporaryDirectory();
        string tools = Directory.CreateDirectory(Path.Combine(temp.Path, "tools")).FullName;
        File.WriteAllText(Path.Combine(tools, "yt-dlp.exe"), string.Empty);
        string downloads = Path.Combine(temp.Path, "downloads");
        var runner = new FakeProcessRunner(new ProcessResult(
            0,
            $"[download] Destination: {Path.Combine(downloads, "clip.mp4")}\r\n[download] 100% of 10MiB\r\n",
            string.Empty));
        var service = new DownloadService(runner, tools, downloads);
        var progressValues = new List<int>();
        var stages = new List<DownloadStage>();

        await service.DownloadAsync(
            "https://example.invalid/video",
            format: DownloadFormat.VideoMp4,
            new InlineProgress<int>(progressValues.Add),
            new InlineProgress<DownloadStage>(stages.Add));

        Assert.Contains(100, progressValues);
        Assert.Contains(DownloadStage.Downloading, stages);
        Assert.Contains(DownloadStage.PostProcessing, stages);
        Assert.Equal(DownloadStage.Done, stages[^1]);
        Assert.Contains("--merge-output-format", runner.StartInfos[0].ArgumentList);
    }

    [Fact]
    public async Task DownloadAsync_CancellationRemovesPartialFile()
    {
        using var temp = new TemporaryDirectory();
        string tools = Directory.CreateDirectory(Path.Combine(temp.Path, "tools")).FullName;
        File.WriteAllText(Path.Combine(tools, "yt-dlp.exe"), string.Empty);
        string downloads = Path.Combine(temp.Path, "downloads");
        string outputPath = Path.Combine(downloads, "partial.mp4");
        string partialPath = outputPath + ".part";
        var runner = new FakeProcessRunner(new ProcessResult(
            0,
            $"[download] Destination: {outputPath}",
            string.Empty))
        {
            CancelAfterOutput = true,
            BeforeReturn = _ => File.WriteAllText(partialPath, "partial")
        };
        var service = new DownloadService(runner, tools, downloads);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(
            "https://example.invalid/video",
            format: DownloadFormat.VideoMp4,
            new InlineProgress<int>(_ => { }),
            new InlineProgress<DownloadStage>(_ => { })));

        Assert.False(File.Exists(partialPath));
    }

    [Fact]
    public async Task MediaConversionService_ReplacesSourceWithMp3Output()
    {
        using var temp = new TemporaryDirectory();
        string tools = Directory.CreateDirectory(Path.Combine(temp.Path, "tools")).FullName;
        File.WriteAllText(Path.Combine(tools, "ffmpeg.exe"), string.Empty);
        string source = Path.Combine(temp.Path, "audio.webm");
        File.WriteAllText(source, "source");
        var runner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty))
        {
            BeforeReturn = startInfo => File.WriteAllText(startInfo.ArgumentList[^1], "mp3")
        };
        var converter = new MediaConversionService(runner, tools);

        await converter.ConvertToAudioAsync(source, DownloadFormat.AudioMp3, CancellationToken.None);

        Assert.False(File.Exists(source));
        Assert.Equal("mp3", File.ReadAllText(Path.ChangeExtension(source, ".mp3")));
    }

    [Fact]
    public async Task DownloadAsync_RequestsWavAudioExtraction()
    {
        using var temp = new TemporaryDirectory();
        string tools = Directory.CreateDirectory(Path.Combine(temp.Path, "tools")).FullName;
        File.WriteAllText(Path.Combine(tools, "yt-dlp.exe"), string.Empty);
        var runner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var service = new DownloadService(runner, tools, Path.Combine(temp.Path, "downloads"));

        await service.DownloadAsync(
            "https://example.invalid/video",
            DownloadFormat.AudioWav,
            new InlineProgress<int>(_ => { }),
            new InlineProgress<DownloadStage>(_ => { }));

        Assert.Contains("--audio-format", runner.StartInfos[0].ArgumentList);
        Assert.Contains("wav", runner.StartInfos[0].ArgumentList);
    }

    [Fact]
    public async Task MediaConversionService_ReplacesSourceWithWavOutput()
    {
        using var temp = new TemporaryDirectory();
        string tools = Directory.CreateDirectory(Path.Combine(temp.Path, "tools")).FullName;
        File.WriteAllText(Path.Combine(tools, "ffmpeg.exe"), string.Empty);
        string source = Path.Combine(temp.Path, "audio.webm");
        File.WriteAllText(source, "source");
        var runner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty))
        {
            BeforeReturn = startInfo => File.WriteAllText(startInfo.ArgumentList[^1], "wav")
        };
        var converter = new MediaConversionService(runner, tools);

        await converter.ConvertToAudioAsync(source, DownloadFormat.AudioWav, CancellationToken.None);

        Assert.False(File.Exists(source));
        Assert.Equal("wav", File.ReadAllText(Path.ChangeExtension(source, ".wav")));
        Assert.Contains("wav", runner.StartInfos[0].ArgumentList);
    }
}

internal sealed class FakeProcessRunner(params ProcessResult[] results) : IExternalProcessRunner
{
    private readonly Queue<ProcessResult> _results = new(results);
    internal List<ProcessStartInfo> StartInfos { get; } = new();
    internal Action<ProcessStartInfo>? BeforeReturn { get; init; }
    internal bool CancelAfterOutput { get; init; }

    public Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        Action<string>? standardOutputLine = null,
        Action<string>? standardErrorLine = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartInfos.Add(startInfo);
        ProcessResult result = _results.Dequeue();
        foreach (string line in result.StandardOutput.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.RemoveEmptyEntries))
            standardOutputLine?.Invoke(line);
        foreach (string line in result.StandardError.Split(
                     new[] { "\r\n", "\n" },
                     StringSplitOptions.RemoveEmptyEntries))
            standardErrorLine?.Invoke(line);
        BeforeReturn?.Invoke(startInfo);
        if (CancelAfterOutput)
            throw new OperationCanceledException(cancellationToken);
        return Task.FromResult(result);
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal sealed class TemporaryDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        Guid.NewGuid().ToString("N"));

    internal TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
