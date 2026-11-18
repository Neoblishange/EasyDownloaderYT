using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace EasyDownloaderYT.Services;

public class AppSettings
{
    public string? Language { get; set; }
}

public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyDownloaderYT",
        "settings.json");

    public static AppSettings Load()
        => Load(FilePath);

    internal static AppSettings Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceError("Could not load application settings from '{0}': {1}", filePath, ex);
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
        => Save(settings, FilePath);

    internal static void Save(AppSettings settings, string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceError("Could not save application settings to '{0}': {1}", filePath, ex);
        }
    }
}