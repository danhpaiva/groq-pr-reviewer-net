using GroqPrReviewer;

// Very large diffs blow past the model's context window.
const int MaxDiffChars = 60_000;

var options = CliOptions.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine(CliOptions.UsageText);
    return 0;
}

if (options.LanguageWarning is not null)
    Console.Error.WriteLine(options.LanguageWarning);

try
{
    var (apiKey, source) = ApiKeyLoader.Load();

    if (options.Check)
        return ReportKeyDiagnostics(apiKey, source, options.Model);

    if (apiKey is null)
    {
        Console.Error.WriteLine("GROQ_API_KEY not found. Set the environment variable or create a .env file (see .env.example).");
        return 1;
    }

    using var client = new GroqClient(apiKey);

    if (options.ListModels)
    {
        foreach (var id in await client.ListModelIdsAsync())
            Console.WriteLine(id);
        return 0;
    }

    var diff = options.DiffFilePath is not null
        ? await File.ReadAllTextAsync(options.DiffFilePath)
        : await GitDiff.RunAsync(options.RepoPath, options.Staged);

    if (string.IsNullOrWhiteSpace(diff))
    {
        Console.WriteLine(options.Staged
            ? "No staged changes found (git diff --staged)."
            : "No changes found (git diff). Tip: use --staged to review what is already staged.");
        return 0;
    }

    if (diff.Length > MaxDiffChars)
    {
        Console.Error.WriteLine($"Warning: diff is {diff.Length} characters; sending only the first {MaxDiffChars}.");
        diff = diff[..MaxDiffChars];
    }

    Console.WriteLine($"Sending diff to Groq for review ({options.Model})...\n");
    Console.WriteLine(await client.ReviewAsync(options.Model, diff, options.Language));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

// Reports enough to debug a setup problem without ever revealing the secret.
static int ReportKeyDiagnostics(string? apiKey, string source, string model)
{
    if (apiKey is null)
    {
        Console.Error.WriteLine("GROQ_API_KEY not found (neither an environment variable nor a .env file).");
        return 1;
    }

    var shape = apiKey.StartsWith("gsk_", StringComparison.Ordinal)
        ? "ok"
        : "UNEXPECTED — Groq keys start with gsk_. Note that Groq (groq.com) is not xAI/Grok (x.ai).";

    Console.WriteLine($"Key source  : {source}");
    Console.WriteLine($"Length      : {apiKey.Length} characters");
    Console.WriteLine($"gsk_ prefix : {shape}");
    Console.WriteLine($"Model       : {model}");
    return 0;
}
