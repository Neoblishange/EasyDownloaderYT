using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace EasyDownloaderYT.Services;

public record LanguageInfo(string Code, string NativeName);

public static class LocalizationService
{
    private const string FallbackLanguage = "en";
    private static ResourceDictionary? _current;

    public static IReadOnlyList<LanguageInfo> Languages { get; } = new[]
    {
        new LanguageInfo("fr", "Français"),
        new LanguageInfo("en", "English"),
        new LanguageInfo("km", "ខ្មែរ")
    };

    public static string CurrentCode { get; private set; } = FallbackLanguage;

    public static event Action? LanguageChanged;

    public static void Initialize()
    {
        string? saved = SettingsService.Load().Language;
        string code = IsSupported(saved)
            ? saved!
            : DetectSystemLanguage();

        SetLanguage(code, persist: false);
    }

    public static void SetLanguage(string code, bool persist = true)
    {
        if (!IsSupported(code))
            code = FallbackLanguage;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/Resources/Languages/Strings.{code}.xaml",
                UriKind.Absolute)
        };

        var merged = Application.Current.Resources.MergedDictionaries;

        if (_current != null)
            merged.Remove(_current);

        merged.Add(dictionary);
        _current = dictionary;
        CurrentCode = code;

        if (persist)
            SettingsService.Save(new AppSettings { Language = code });

        LanguageChanged?.Invoke();
    }

    public static string T(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    public static string T(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    private static bool IsSupported(string? code) =>
        code != null && Languages.Any(l => l.Code == code);

    private static string DetectSystemLanguage()
    {
        string system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return IsSupported(system) ? system : FallbackLanguage;
    }
}