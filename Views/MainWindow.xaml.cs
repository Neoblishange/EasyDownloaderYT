using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyDownloaderYT.Services;
using EasyDownloaderYT.ViewModels;
using static EasyDownloaderYT.Services.LocalizationService;

namespace EasyDownloaderYT.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        UrlTextBox.PreviewMouseDown += (_, _) => NotificationBanner.Hide();
        LanguageList.ItemsSource = Languages;
        LanguageList.SelectedValue = CurrentCode;
        LanguageList.SelectionChanged += LanguageList_SelectionChanged;
        _viewModel.NotificationRequested += ViewModel_NotificationRequested;
        PreviewMouseDown += (_, _) =>
        {
            if (NotificationBanner.DismissOnClick)
                NotificationBanner.Hide();
        };
        DownloadButton.Click += DownloadButton_Click;
        PasteButton.Click += PasteButton_Click;
        OpenFolderButton.Click += OpenFolderButton_Click;
        UpdateButton.Click += UpdateButton_Click;
        FormatComboBox.SelectionChanged += (_, _) => ApplyAccent();
        ApplyAccent();
        Closed += (_, _) =>
        {
            _viewModel.NotificationRequested -= ViewModel_NotificationRequested;
            _viewModel.Dispose();
        };
    }

    private void LanguageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageList.SelectedValue is string code)
            SetLanguage(code);
    }

    private void ViewModel_NotificationRequested(NotificationRequest request)
    {
        string message = request.Error is not null
            ? MainViewModel.BuildErrorMessage(request.Error)
            : request.ResourceKey is not null
                ? T(request.ResourceKey)
                : T("Error.Unknown");

        NotificationBanner.Show(
            message,
            request.Kind,
            request.ResourceKey,
            request.Error,
            request.DismissOnClick,
            request.DismissOnPaste,
            request.Duration);
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        if (NotificationBanner.DismissOnPaste)
            NotificationBanner.Hide();

        try
        {
            if (Clipboard.ContainsText())
            {
                UrlTextBox.Text = Clipboard.GetText().Trim();
                UrlTextBox.CaretIndex = UrlTextBox.Text.Length;
            }
            else
            {
                ShowInfo("Msg.ClipboardEmpty.Text");
            }
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            MessageBox.Show(
                T("Msg.ClipboardError.Text"),
                T("Msg.ClipboardError.Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "EasyDownloaderYT");

        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            ViewModel_NotificationRequested(new(
                NotificationKind.Error,
                Error: ex,
                DismissOnPaste: true,
                Duration: TimeSpan.FromSeconds(10)));
        }
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        string url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowInfo("Msg.LinkMissing.Text");
            return;
        }

        DownloadFormat format = FormatComboBox.SelectedIndex switch
        {
            1 => DownloadFormat.AudioMp3,
            2 => DownloadFormat.AudioWav,
            _ => DownloadFormat.VideoMp4
        };
        if (_viewModel.AddDownload(url, format))
            UrlTextBox.Clear();
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.UpdateYtDlpAsync();

    private void ShowInfo(string textKey) =>
        NotificationBanner.Show(
            T(textKey),
            NotificationKind.Info,
            textKey,
            duration: TimeSpan.FromSeconds(10),
            dismissOnPaste: true);

    private void ApplyAccent()
    {
        bool audio = FormatComboBox.SelectedIndex != 0;
        Resources["AccentBrush"] = Brush(audio ? "#7C3AED" : "#2563EB");
        Resources["AccentHoverBrush"] = Brush(audio ? "#6D28D9" : "#1D4ED8");
        Resources["AccentPressedBrush"] = Brush(audio ? "#5B21B6" : "#1E40AF");
    }

    private static SolidColorBrush Brush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
