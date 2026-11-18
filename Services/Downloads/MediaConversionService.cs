using System.Diagnostics;
using System.IO;

namespace EasyDownloaderYT.Services;

internal sealed class MediaConversionService
{
    private readonly IExternalProcessRunner _processRunner;
    private readonly string _toolsPath;

    internal MediaConversionService(IExternalProcessRunner processRunner, string toolsPath)
    {
        _processRunner = processRunner;
        _toolsPath = toolsPath;
    }

    internal async Task ConvertToAudioAsync(
        string sourcePath,
        DownloadFormat format,
        CancellationToken cancellationToken)
    {
        string ffmpegPath = Path.Combine(_toolsPath, "ffmpeg.exe");
        if (!File.Exists(ffmpegPath))
            throw new DownloadException("Error.ToolMissing");

        string extension = format switch
        {
            DownloadFormat.AudioMp3 => ".mp3",
            DownloadFormat.AudioWav => ".wav",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        string outputPath = Path.ChangeExtension(sourcePath, extension);
        string temporaryOutputPath = outputPath + ".tmp";
        var startInfo = new ProcessStartInfo { FileName = ffmpegPath };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-vn");
        if (format == DownloadFormat.AudioMp3)
        {
            startInfo.ArgumentList.Add("-codec:a");
            startInfo.ArgumentList.Add("libmp3lame");
            startInfo.ArgumentList.Add("-q:a");
            startInfo.ArgumentList.Add("0");
        }
        startInfo.ArgumentList.Add("-map_metadata");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(format == DownloadFormat.AudioWav ? "wav" : "mp3");
        startInfo.ArgumentList.Add(temporaryOutputPath);

        try
        {
            ProcessResult result = await _processRunner.RunAsync(startInfo, cancellationToken: cancellationToken);
            if (result.ExitCode != 0)
            {
                if (File.Exists(temporaryOutputPath))
                    File.Delete(temporaryOutputPath);
                throw new DownloadException("Error.Ffmpeg", result.StandardError.Trim());
            }

            File.Move(temporaryOutputPath, outputPath, overwrite: false);
            File.Delete(sourcePath);
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(temporaryOutputPath))
                File.Delete(temporaryOutputPath);
            throw;
        }
    }
}
