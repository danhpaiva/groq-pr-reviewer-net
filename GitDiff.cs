using System.Diagnostics;

namespace GroqPrReviewer;

/// <summary>Reads a diff out of a git repository.</summary>
internal static class GitDiff
{
    public static async Task<string> RunAsync(string repoPath, bool staged)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = staged ? "diff --staged" : "diff",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start the git process. Is git on your PATH?");

        // Drain both streams concurrently before waiting: sequential reads deadlock
        // if stderr fills its pipe buffer while we are still reading stdout.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git diff failed: {await errorTask}");

        return await outputTask;
    }
}
