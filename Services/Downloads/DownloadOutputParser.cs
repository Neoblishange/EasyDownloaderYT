using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace EasyDownloaderYT.Services;

internal static class DownloadOutputParser
{
    private static readonly Regex ProgressPattern = new(
        @"(\d{1,3}(?:\.\d+)?)%",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DownloadCompletePattern = new(
        @"^\[download\]\s+100(?:\.0+)?%",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static int? ParseProgress(string line)
    {
        Match match = ProgressPattern.Match(line);
        return match.Success
            && double.TryParse(
                match.Groups[1].Value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out double percentage)
            ? Math.Clamp((int)percentage, 0, 100)
            : null;
    }

    internal static bool IsDownloadComplete(string line) => DownloadCompletePattern.IsMatch(line);

    internal static string? GetOutputPath(string line, string downloadPath)
    {
        const string marker = "Destination:";
        int markerIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return null;

        string path = line[(markerIndex + marker.Length)..].Trim().Trim('"');
        string fullPath = Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(downloadPath, path));
        string root = Path.GetFullPath(downloadPath) + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }
}
