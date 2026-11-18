using EasyDownloaderYT.Services;
using Xunit;

namespace EasyDownloaderYT.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "nested", "settings.json");

        SettingsService.Save(new AppSettings { Language = "fr" }, file);

        Assert.Equal("fr", SettingsService.Load(file).Language);
    }

    [Fact]
    public void Load_InvalidJsonReturnsDefaultSettings()
    {
        using var temp = new TemporaryDirectory();
        string file = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(file, "{ invalid");

        Assert.Null(SettingsService.Load(file).Language);
    }
}
