using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using EasyDownloaderYT.Services;

namespace EasyDownloaderYT.ViewModels;

public enum DownloadQueueItemState
{
    Validating,
    Queued,
    Downloading,
    PostProcessing,
    Completed,
    Failed,
    Cancelled
}

public sealed class DownloadQueueItemViewModel : INotifyPropertyChanged
{
    private readonly CancellationTokenSource _cancellationSource = new();
    private DownloadQueueItemState _state = DownloadQueueItemState.Queued;
    private int _progress;
    private string? _title;
    private string _displayTitle = string.Empty;
    private string _statusText = string.Empty;
    private string _formatText = string.Empty;
    private string? _errorMessage;

    public DownloadQueueItemViewModel(string url, DownloadFormat format, string? title)
    {
        Url = url;
        Format = format;
        Title = title;
    }

    public string Url { get; }
    public DownloadFormat Format { get; }
    public CancellationToken CancellationToken => _cancellationSource.Token;
    public Exception? Error { get; set; }
    public bool CanCancel => State is DownloadQueueItemState.Validating
        or DownloadQueueItemState.Queued
        or DownloadQueueItemState.Downloading
        or DownloadQueueItemState.PostProcessing;
    public bool CanRetry => State == DownloadQueueItemState.Failed;

    public string? Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string DisplayTitle
    {
        get => _displayTitle;
        set => SetProperty(ref _displayTitle, value);
    }

    public DownloadQueueItemState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string FormatText
    {
        get => _formatText;
        set => SetProperty(ref _formatText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public void Cancel() => _cancellationSource.Cancel();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(State))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCancel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
        }
    }
}
