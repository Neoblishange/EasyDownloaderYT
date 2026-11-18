using System.Diagnostics;
using System.IO;
using System.Text;

namespace EasyDownloaderYT.Services;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal interface IExternalProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        Action<string>? standardOutputLine = null,
        Action<string>? standardErrorLine = null,
        CancellationToken cancellationToken = default);
}

internal sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        Action<string>? standardOutputLine = null,
        Action<string>? standardErrorLine = null,
        CancellationToken cancellationToken = default)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        Task outputTask = ReadLinesAsync(process.StandardOutput, standardOutput, standardOutputLine);
        Task errorTask = ReadLinesAsync(process.StandardError, standardError, standardErrorLine);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        StringBuilder content,
        Action<string>? lineHandler)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            content.AppendLine(line);
            lineHandler?.Invoke(line);
        }
    }
}
