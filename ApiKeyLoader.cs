namespace GroqPrReviewer;

/// <summary>Resolves GROQ_API_KEY from the environment or a .env file.</summary>
internal static class ApiKeyLoader
{
    private const string VariableName = "GROQ_API_KEY";

    /// <summary>
    /// Returns the key and a human-readable description of where it came from.
    /// The source matters: an environment variable silently wins over a .env
    /// file, which is a common reason for "but I just changed my key".
    /// </summary>
    public static (string? Key, string Source) Load()
    {
        var fromEnv = Environment.GetEnvironmentVariable(VariableName);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return (fromEnv.Trim(), $"{VariableName} environment variable");

        var envPath = FindEnvFile();
        if (envPath is null)
            return (null, "none");

        foreach (var rawLine in File.ReadAllLines(envPath))
        {
            // A BOM sticks to the first line when the .env is saved as UTF-8 with BOM.
            var line = rawLine.Trim().TrimStart('﻿').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line[7..].TrimStart();

            var parts = line.Split('=', 2);
            if (parts.Length != 2 || parts[0].Trim() != VariableName)
                continue;

            var value = ParseValue(parts[1]);
            return string.IsNullOrWhiteSpace(value) ? (null, "none") : (value, envPath);
        }

        return (null, "none");
    }

    /// <summary>
    /// Walks up from both the current directory and the binary's directory, so
    /// this works with `dotnet run` as well as a published executable.
    /// </summary>
    private static string? FindEnvFile()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }

    internal static string ParseValue(string raw)
    {
        var value = raw.Trim();

        // Quoted value: return the literal contents, '#' included.
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];

        // Unquoted, a '#' preceded by a space starts a comment.
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0)
            value = value[..comment];

        return value.Trim();
    }
}
