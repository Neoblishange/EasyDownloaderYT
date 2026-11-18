namespace EasyDownloaderYT.Services;

internal static class DownloadErrorMapper
{
    private static readonly ErrorRule[] Rules =
    {
        new("Error.InvalidVideoId",
            "invalid video id",
            "video id is invalid",
            "video id does not exist",
            "no video with id",
            "incomplete youtube id",
            "youtube:truncated_id"),
        new("Error.InvalidUrl",
            "not a valid URL",
            "Unsupported URL"),
        new("Error.AgeRestricted",
            "confirm your age",
            "age-restricted",
            "age restricted"),
        new("Error.NotABot",
            "not a bot",
            "confirm you're not a bot",
            "confirm you are not a bot"),
        new("Error.Private",
            "Private video",
            "This video is private"),
        new("Error.MembersOnly",
            "members-only",
            "members only",
            "Join this channel"),
        new("Error.GeoBlocked",
            "not available in your country",
            "geo-restricted",
            "georestricted",
            "geographic restriction",
            "blocked in your country",
            "not available from your location"),
        new("Error.NotStarted",
            "Premieres in",
            "live event will begin",
            "This live event will begin"),
        new("Error.Live",
            " is live",
            "live stream",
            "currently live"),
        new("Error.FormatUnavailable",
            "Requested format is not available",
            "Requested format not available"),
        new("Error.Unavailable",
            "Video unavailable",
            "video is unavailable",
            "not available",
            "has been removed",
            "video was removed"),
        new("Error.RateLimit",
            "HTTP Error 429",
            "Too Many Requests",
            "rate limit"),
        new("Error.Network",
            "Unable to download",
            "getaddrinfo",
            "urlopen error",
            "timed out",
            "connection reset",
            "connection refused",
            "network is unreachable",
            "temporary failure in name resolution"),
        new("Error.Ffmpeg",
            "ffmpeg",
            "ffprobe",
            "postprocessing"),
        new("Error.LoginRequired",
            "sign in",
            "login required",
            "only available to registered users")
    };

    internal static string MapErrorKey(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "Error.Unknown";

        foreach (ErrorRule rule in Rules)
        {
            if (rule.Matches(error))
                return rule.ResourceKey;
        }

        return "Error.Failed";
    }

    private sealed record ErrorRule(string ResourceKey, params string[] Indicators)
    {
        internal bool Matches(string error) =>
            Indicators.Any(indicator =>
                error.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }
}
