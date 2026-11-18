using System;

namespace EasyDownloaderYT.Services;

public enum DownloadStage
{
    Downloading,
    PostProcessing,
    Done
}

public enum UpdateResult
{
    UpToDate,
    Updated
}

public enum DownloadFormat
{
    VideoMp4,
    AudioMp3,
    AudioWav
}

public class DownloadException : Exception
{
    public string ResourceKey { get; }
    public string? TechnicalDetail { get; }

    public DownloadException(string resourceKey, string? technicalDetail = null)
        : base(resourceKey)
    {
        ResourceKey = resourceKey;
        TechnicalDetail = technicalDetail;
    }
}

internal interface IDownloadService
{
    Task<string> GetTitleAsync(string url, CancellationToken cancellationToken = default);

    Task DownloadAsync(
        string url,
        DownloadFormat format,
        IProgress<int> progress,
        IProgress<DownloadStage> stage,
        CancellationToken cancellationToken = default);

    Task<UpdateResult> UpdateYtDlpAsync();
}