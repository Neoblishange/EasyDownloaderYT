using System.Windows;
using EasyDownloaderYT.Services;

namespace EasyDownloaderYT;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LocalizationService.Initialize();
        base.OnStartup(e);
    }
}