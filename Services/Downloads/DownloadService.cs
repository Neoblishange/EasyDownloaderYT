using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyDownloaderYT.Services;

public class DownloadService : IDownloadService
{
    private readonly IExternalProcessRunner _processRunner;
    private readonly MediaConversionService _mediaConversionService;
    private readonly string _toolsPath;
    private readonly string _downloadPath;

    public DownloadService()
        : this(
            new ExternalProcessRunner(),
            Path.Combine(AppContext.BaseDirectory, "Resources", "Tools"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "EasyDownloaderYT"))
    {
    }

    internal DownloadService(IExternalProcessRunner processRunner, string toolsPath, string downloadPath)
    {
        _processRunner = processRunner;
        _toolsPath = toolsPath;
        _downloadPath = downloadPath;
        _mediaConversionService = new MediaConversionService(processRunner, toolsPath);
    }

    public async Task<string> GetTitleAsync(string url, CancellationToken cancellationToken = default)
    {
        string ytDlpPath = Path.Combine(_toolsPath, "yt-dlp.exe");
        string denoPath = Path.Combine(_toolsPath, "deno.exe");

        if (!File.Exists(ytDlpPath))
            throw new DownloadException("Error.ToolMissing");

        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--encoding");
        startInfo.ArgumentList.Add("utf-8");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--get-title");

        if (File.Exists(denoPath))
        {
            startInfo.ArgumentList.Add("--js-runtimes");
            startInfo.ArgumentList.Add($"deno:{denoPath}");
        }

        startInfo.ArgumentList.Add(url);

        ProcessResult result = await _processRunner.RunAsync(startInfo, cancellationToken: cancellationToken);
        if (result.ExitCode != 0)
        {
            string? errorLine = result.StandardError
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(line => line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));
            string errorKey = MapErrorKey(errorLine);
            throw new DownloadException(errorKey, errorLine ?? result.StandardError.Trim());
        }

        string? title = result.StandardOutput
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(title))
            throw new DownloadException("Error.Unknown", "yt-dlp returned no title.");

        return title.Trim();
    }

    public async Task DownloadAsync(
        string url,
        DownloadFormat format,
        IProgress<int> progress,
        IProgress<DownloadStage> stage,
        CancellationToken cancellationToken = default)
    {
        string ytDlpPath = Path.Combine(_toolsPath, "yt-dlp.exe");
        string denoPath = Path.Combine(_toolsPath, "deno.exe");

        if (!File.Exists(ytDlpPath))
            throw new DownloadException("Error.ToolMissing");

        Directory.CreateDirectory(_downloadPath);
        var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object outputPathsLock = new();

        async Task<(int ExitCode, string? Error, List<string> Destinations)> RunYtDlpAsync(
            bool extractAudio)
        {
            bool audio = format != DownloadFormat.VideoMp4;
            var startInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--newline");
            startInfo.ArgumentList.Add("--progress");
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("--windows-filenames");

            startInfo.ArgumentList.Add("-P");
            startInfo.ArgumentList.Add(_downloadPath);

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("%(title)s.%(ext)s");

            startInfo.ArgumentList.Add("--ffmpeg-location");
            startInfo.ArgumentList.Add(_toolsPath);

            if (File.Exists(denoPath))
            {
                startInfo.ArgumentList.Add("--js-runtimes");
                startInfo.ArgumentList.Add($"deno:{denoPath}");
            }

            if (audio)
            {
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("bestaudio/best");

                if (extractAudio)
                {
                    startInfo.ArgumentList.Add("-x");
                    startInfo.ArgumentList.Add("--audio-format");
                    startInfo.ArgumentList.Add(format == DownloadFormat.AudioWav ? "wav" : "mp3");

                    startInfo.ArgumentList.Add("--audio-quality");
                    startInfo.ArgumentList.Add("0");
                }
            }
            else
            {
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("bestvideo*+bestaudio/best");

                startInfo.ArgumentList.Add("--merge-output-format");
                startInfo.ArgumentList.Add("mp4");
            }

            startInfo.ArgumentList.Add(url);

            string? lastError = null;
            var destinations = new List<string>();

            void HandleLine(string line)
            {
                if (DownloadOutputParser.IsDownloadComplete(line))
                    stage.Report(DownloadStage.PostProcessing);

                string? outputPath = DownloadOutputParser.GetOutputPath(line, _downloadPath);
                if (outputPath is not null)
                {
                    lock (outputPathsLock)
                    {
                        outputPaths.Add(outputPath);
                        destinations.Add(outputPath);
                    }
                }

                if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                {
                    lock (outputPathsLock)
                        lastError = line;
                    return;
                }

                int? percentage = DownloadOutputParser.ParseProgress(line);
                if (percentage is int value)
                    progress.Report(value);
            }

            stage.Report(DownloadStage.Downloading);
            ProcessResult result = await _processRunner.RunAsync(
                startInfo,
                HandleLine,
                HandleLine,
                cancellationToken);

            return (result.ExitCode, lastError, destinations);
        }

        try
        {
            if (format != DownloadFormat.VideoMp4)
            {
                var audioResult = await RunYtDlpAsync(extractAudio: true);
                if (audioResult.ExitCode != 0)
                {
                    var sourceResult = await RunYtDlpAsync(extractAudio: false);
                    if (sourceResult.ExitCode != 0)
                        throw new DownloadException(MapErrorKey(sourceResult.Error), sourceResult.Error);

                    string? sourcePath = sourceResult.Destinations
                        .Concat(audioResult.Destinations)
                        .FirstOrDefault(path =>
                            !string.Equals(
                                Path.GetExtension(path),
                                format == DownloadFormat.AudioWav ? ".wav" : ".mp3",
                                StringComparison.OrdinalIgnoreCase)
                            && File.Exists(path));

                    if (sourcePath == null)
                        throw new DownloadException("Error.Failed", "Could not locate the downloaded audio stream.");

                    stage.Report(DownloadStage.PostProcessing);
                    await _mediaConversionService.ConvertToAudioAsync(sourcePath, format, cancellationToken);
                }
            }
            else
            {
                var result = await RunYtDlpAsync(extractAudio: false);
                if (result.ExitCode != 0)
                    throw new DownloadException(MapErrorKey(result.Error), result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            DeletePartialFiles(outputPaths, outputPathsLock);
            throw;
        }

        progress.Report(100);
        stage.Report(DownloadStage.Done);
    }

    private static void DeletePartialFiles(HashSet<string> outputPaths, object outputPathsLock)
    {
        string[] paths;
        lock (outputPathsLock)
            paths = outputPaths.ToArray();

        foreach (string path in paths)
        {
            string partialPath = path + ".part";
            if (File.Exists(partialPath))
                File.Delete(partialPath);
        }
    }

    public async Task<UpdateResult> UpdateYtDlpAsync()
    {
        string ytDlpPath = Path.Combine(_toolsPath, "yt-dlp.exe");

        if (!File.Exists(ytDlpPath))
            throw new DownloadException("Error.ToolMissing");

        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-U");

        ProcessResult result = await _processRunner.RunAsync(startInfo);
        if (result.ExitCode != 0)
            throw new DownloadException("Update.Failed", (result.StandardError + result.StandardOutput).Trim());

        return result.StandardOutput.Contains("up to date", StringComparison.OrdinalIgnoreCase)
            ? UpdateResult.UpToDate
            : UpdateResult.Updated;
    }

    internal static string MapErrorKey(string? error) => DownloadErrorMapper.MapErrorKey(error);
}