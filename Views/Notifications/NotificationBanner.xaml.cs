using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EasyDownloaderYT.ViewModels;
using static EasyDownloaderYT.Services.LocalizationService;

namespace EasyDownloaderYT.Views.Notifications;

public partial class NotificationBanner : UserControl
{
    private readonly DispatcherTimer _notificationTimer = new()
    {
        Interval = TimeSpan.FromSeconds(4)
    };
    private string? _notificationTextKey;
    private Exception? _notificationError;
    private bool _dismissOnClick;
    private bool _dismissOnPaste;

    public NotificationBanner()
    {
        InitializeComponent();
        _notificationTimer.Tick += (_, _) => Hide();
        LanguageChanged += UpdateVisibleNotification;
        Unloaded += (_, _) =>
        {
            LanguageChanged -= UpdateVisibleNotification;
            _notificationTimer.Stop();
        };
    }

    public bool DismissOnPaste => _dismissOnPaste;
    public bool DismissOnClick => _dismissOnClick;

    public void Show(
        string message,
        NotificationKind kind,
        string? textKey = null,
        Exception? error = null,
        bool dismissOnClick = false,
        bool dismissOnPaste = false,
        TimeSpan? duration = null)
    {
        _notificationTextKey = textKey;
        _notificationError = error;
        _dismissOnClick = dismissOnClick;
        _dismissOnPaste = dismissOnPaste;

        bool isError = kind == NotificationKind.Error;
        bool isInfo = kind == NotificationKind.Info;
        NotificationText.Text = message;
        NotificationText.Foreground = Brush(isError ? "#B91C1C" : isInfo ? "#1D4ED8" : "#047857");
        AppNotification.Background = Brush(isError ? "#FEF2F2" : isInfo ? "#EFF6FF" : "#ECFDF5");
        AppNotification.BorderBrush = Brush(isError ? "#FECACA" : isInfo ? "#BFDBFE" : "#A7F3D0");
        AppNotification.Visibility = Visibility.Visible;
        _notificationTimer.Stop();
        if (!dismissOnClick)
        {
            _notificationTimer.Interval = duration ?? TimeSpan.FromSeconds(4);
            _notificationTimer.Start();
        }
    }

    public void Hide()
    {
        _notificationTimer.Stop();
        _notificationError = null;
        _notificationTextKey = null;
        _dismissOnClick = false;
        _dismissOnPaste = false;
        AppNotification.Visibility = Visibility.Collapsed;
    }

    private void AppNotification_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_dismissOnClick)
            Hide();
    }

    private void UpdateVisibleNotification()
    {
        if (AppNotification.Visibility != Visibility.Visible)
            return;

        if (_notificationError is not null)
            NotificationText.Text = MainViewModel.BuildErrorMessage(_notificationError);
        else if (_notificationTextKey is not null)
            NotificationText.Text = T(_notificationTextKey);
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
