using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EasyDownloaderYT.Services;
using static EasyDownloaderYT.Services.LocalizationService;

namespace EasyDownloaderYT.ViewModels;

public enum NotificationKind
{
    Success,
    Info,
    Error
}

public sealed record NotificationRequest(
    NotificationKind Kind,
    string? ResourceKey = null,
    Exception? Error = null,
    bool DismissOnClick = false,
    bool DismissOnPaste = false,
    TimeSpan? Duration = null);

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDownloadService _downloadService;
    private readonly Func<string, string> _translate;
    private readonly Func<Exception, string> _errorMessageBuilder;
    private readonly TimeSpan _minimumDownloadInterval;
    private bool _isUpdating;
    private bool _isProcessingQueue;
    private bool _isQueueEmpty = true;
    private bool _canUpdate = true;
    private DateTimeOffset? _lastDownloadCompletedAt;

    public MainViewModel(DownloadService? downloadService = null)
        : this(downloadService ?? new DownloadService(), T, BuildErrorMessage, TimeSpan.FromSeconds(10))
    {
    }

    internal MainViewModel(
        IDownloadService downloadService,
        Func<string, string> translate,
        Func<Exception, string>? errorMessageBuilder = null,
        TimeSpan? minimumDownloadInterval = null)
    {
        _downloadService = downloadService;
        _translate = translate;
        _errorMessageBuilder = errorMessageBuilder ?? BuildErrorMessage;
        _minimumDownloadInterval = minimumDownloadInterval ?? TimeSpan.FromSeconds(10);
        CancelCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is DownloadQueueItemViewModel item)
                    Cancel(item);
            },
            parameter => parameter is DownloadQueueItemViewModel);
        RetryCommand = new AsyncRelayCommand(
            async parameter =>
            {
                if (parameter is DownloadQueueItemViewModel item)
                    await RetryAsync(item);
            },
            parameter => parameter is DownloadQueueItemViewModel);
        ClearFinishedItemsCommand = new RelayCommand(_ => ClearFinishedItems());
        DownloadQueue.CollectionChanged += DownloadQueue_CollectionChanged;
        LanguageChanged += RefreshLocalizedText;
    }

    public ObservableCollection<DownloadQueueItemViewModel> DownloadQueue { get; } = new();
    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand ClearFinishedItemsCommand { get; }

    public bool IsQueueEmpty
    {
        get => _isQueueEmpty;
        private set => SetProperty(ref _isQueueEmpty, value);
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        private set
        {
            if (SetProperty(ref _isUpdating, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                UpdateAvailability();
            }
        }
    }

    public bool CanInteract => !IsUpdating;

    public bool CanUpdate
    {
        get => _canUpdate;
        private set => SetProperty(ref _canUpdate, value);
    }

    public event Action<NotificationRequest>? NotificationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool AddDownload(string url, DownloadFormat format)
    {
        url = url.Trim();
        if (!IsValidHttpUrl(url))
        {
            NotificationRequested?.Invoke(new(
                NotificationKind.Error,
                ResourceKey: "Error.InvalidUrl",
                Duration: TimeSpan.FromSeconds(10)));
            return false;
        }

        if (IsUpdating)
            return false;

        var item = new DownloadQueueItemViewModel(url, format, null)
        {
            State = DownloadQueueItemState.Validating
        };
        DownloadQueue.Insert(0, item);
        UpdateQueueItemText(item);
        _ = ValidateAndQueueItemAsync(item);
        return true;
    }

    public void Cancel(DownloadQueueItemViewModel item)
    {
        item.Cancel();
        if (item.State is DownloadQueueItemState.Validating or DownloadQueueItemState.Queued)
        {
            DownloadQueue.Remove(item);
            return;
        }

        UpdateAvailability();
    }

    public async Task RetryAsync(DownloadQueueItemViewModel item)
    {
        if (item.State != DownloadQueueItemState.Failed || IsUpdating)
            return;

        item.Error = null;
        item.Progress = 0;
        item.State = DownloadQueueItemState.Validating;
        UpdateQueueItemText(item);
        await ValidateAndQueueItemAsync(item);
    }

    public void ClearFinishedItems()
    {
        DownloadQueueItemViewModel[] finishedItems = DownloadQueue
            .Where(item => item.State is DownloadQueueItemState.Completed
                or DownloadQueueItemState.Cancelled
                or DownloadQueueItemState.Failed)
            .ToArray();

        foreach (DownloadQueueItemViewModel item in finishedItems)
            DownloadQueue.Remove(item);
    }

    public async Task UpdateYtDlpAsync()
    {
        if (!CanUpdate)
            return;

        IsUpdating = true;
        try
        {
            UpdateResult result = await _downloadService.UpdateYtDlpAsync();
            string key = result == UpdateResult.UpToDate
                ? "Update.UpToDate"
                : "Update.Updated";
            NotificationRequested?.Invoke(new(
                NotificationKind.Success,
                ResourceKey: key));
        }
        catch (Exception ex)
        {
            NotificationRequested?.Invoke(new(
                NotificationKind.Error,
                Error: ex,
                DismissOnPaste: true,
                Duration: TimeSpan.FromSeconds(10)));
        }
        finally
        {
            IsUpdating = false;
            _ = ProcessDownloadQueueAsync();
        }
    }

    public void RefreshLocalizedText()
    {
        foreach (DownloadQueueItemViewModel item in DownloadQueue)
            UpdateQueueItemText(item);
    }

    public static string BuildErrorMessage(Exception ex) =>
        ex is DownloadException downloadException
            ? T(downloadException.ResourceKey)
            : T("Error.Unknown");

    public void Dispose()
    {
        LanguageChanged -= RefreshLocalizedText;
        DownloadQueue.CollectionChanged -= DownloadQueue_CollectionChanged;
        foreach (DownloadQueueItemViewModel item in DownloadQueue)
            item.PropertyChanged -= QueueItem_PropertyChanged;
    }

    private async Task ValidateAndQueueItemAsync(DownloadQueueItemViewModel item)
    {
        try
        {
            item.Title = await _downloadService.GetTitleAsync(item.Url, item.CancellationToken);
            item.CancellationToken.ThrowIfCancellationRequested();
            item.State = DownloadQueueItemState.Queued;
            UpdateQueueItemText(item);
            await ProcessDownloadQueueAsync();
        }
        catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
        {
            DownloadQueue.Remove(item);
        }
        catch (Exception ex)
        {
            item.Error = ex;
            item.State = DownloadQueueItemState.Failed;
            UpdateQueueItemText(item);
            NotificationRequested?.Invoke(new(
                NotificationKind.Error,
                Error: ex,
                DismissOnPaste: true,
                Duration: TimeSpan.FromSeconds(10)));
        }
    }

    private async Task ProcessDownloadQueueAsync()
    {
        if (_isProcessingQueue || IsUpdating)
            return;

        _isProcessingQueue = true;
        UpdateAvailability();
        try
        {
            while (true)
            {
                DownloadQueueItemViewModel? item = DownloadQueue.LastOrDefault(
                    queuedItem => queuedItem.State == DownloadQueueItemState.Queued);
                if (item is null)
                    break;

                if (_lastDownloadCompletedAt is DateTimeOffset completedAt)
                {
                    TimeSpan remainingDelay = _minimumDownloadInterval
                        - (DateTimeOffset.UtcNow - completedAt);
                    if (remainingDelay > TimeSpan.Zero)
                        await Task.Delay(remainingDelay);

                    item = DownloadQueue.LastOrDefault(
                        queuedItem => queuedItem.State == DownloadQueueItemState.Queued);
                    if (item is null)
                        continue;
                }

                item.State = DownloadQueueItemState.Downloading;
                item.Progress = 0;
                UpdateQueueItemText(item);
                var progress = new Progress<int>(value => item.Progress = value);
                var stage = new Progress<DownloadStage>(value =>
                {
                    if (value == DownloadStage.PostProcessing)
                    {
                        item.State = DownloadQueueItemState.PostProcessing;
                        UpdateQueueItemText(item);
                    }
                    else if (value == DownloadStage.Downloading &&
                             item.State == DownloadQueueItemState.PostProcessing)
                    {
                        item.State = DownloadQueueItemState.Downloading;
                        UpdateQueueItemText(item);
                    }
                });

                try
                {
                    await _downloadService.DownloadAsync(
                        item.Url,
                        item.Format,
                        progress,
                        stage,
                        item.CancellationToken);
                    item.State = DownloadQueueItemState.Completed;
                    item.Progress = 100;
                    NotificationRequested?.Invoke(new(
                        NotificationKind.Success,
                        ResourceKey: "Status.Done",
                        DismissOnClick: true));
                }
                catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
                {
                    item.State = DownloadQueueItemState.Cancelled;
                }
                catch (Exception ex)
                {
                    item.Error = ex;
                    item.State = DownloadQueueItemState.Failed;
                    NotificationRequested?.Invoke(new(
                        NotificationKind.Error,
                        Error: ex,
                        DismissOnPaste: true,
                        Duration: TimeSpan.FromSeconds(10)));
                }

                _lastDownloadCompletedAt = DateTimeOffset.UtcNow;
                UpdateQueueItemText(item);
            }
        }
        finally
        {
            _isProcessingQueue = false;
            UpdateAvailability();
        }
    }

    private void DownloadQueue_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DownloadQueueItemViewModel item in e.OldItems)
                item.PropertyChanged -= QueueItem_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (DownloadQueueItemViewModel item in e.NewItems)
                item.PropertyChanged += QueueItem_PropertyChanged;
        }

        IsQueueEmpty = DownloadQueue.Count == 0;
        UpdateAvailability();
    }

    private void QueueItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadQueueItemViewModel.State))
            UpdateAvailability();
    }

    private void UpdateAvailability()
    {
        CanUpdate = !IsUpdating
            && !_isProcessingQueue
            && !DownloadQueue.Any(item => item.State is DownloadQueueItemState.Validating
                or DownloadQueueItemState.Queued
                or DownloadQueueItemState.Downloading
                or DownloadQueueItemState.PostProcessing);
    }

    private void UpdateQueueItemText(DownloadQueueItemViewModel item)
    {
        item.DisplayTitle = item.State == DownloadQueueItemState.Validating
            ? _translate("Queue.Validating")
            : item.Title ?? _translate("Queue.TitleUnavailable");
        item.FormatText = _translate(item.Format switch
        {
            DownloadFormat.VideoMp4 => "Format.Video",
            DownloadFormat.AudioMp3 => "Format.AudioMp3",
            DownloadFormat.AudioWav => "Format.AudioWav",
            _ => throw new ArgumentOutOfRangeException(nameof(item.Format))
        });
        item.StatusText = _translate(item.State switch
        {
            DownloadQueueItemState.Validating => "Queue.Validating",
            DownloadQueueItemState.Queued => "Queue.Waiting",
            DownloadQueueItemState.Downloading => "Queue.Downloading",
            DownloadQueueItemState.PostProcessing => "Queue.PostProcessing",
            DownloadQueueItemState.Completed => "Queue.Done",
            DownloadQueueItemState.Failed => "Queue.Failed",
            DownloadQueueItemState.Cancelled => "Queue.Cancelled",
            _ => "Queue.Waiting"
        });
        item.ErrorMessage = item.Error is null ? null : _errorMessageBuilder(item.Error);
    }

    private static bool IsValidHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
