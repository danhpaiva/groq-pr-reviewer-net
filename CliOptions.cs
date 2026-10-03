namespace GroqPrReviewer;

/// <summary>Command line arguments, already parsed and defaulted.</summary>
internal sealed class CliOptions
{
    // Open-weight (Apache 2.0) and currently the strongest chat model on Groq.
    // Run --list-models if this one is ever retired, then pass --model.
    public const string DefaultModel = "openai/gpt-oss-120b";

    public bool ShowHelp { get; private init; }
    public bool Staged { get; private init; }
    public bool Check { get; private init; }
    public bool ListModels { get; private init; }
    public string RepoPath { get; private init; } = string.Empty;
    public string Model { get; private init; } = DefaultModel;
    public ReviewLanguage Language { get; private init; } = ReviewLanguage.English;
    public string? LanguageWarning { get; private init; }
    public string? DiffFilePath { get; private init; }

    public static CliOptions Parse(string[] args)
    {
        var showHelp = false;
        var staged = false;
        var check = false;
        var listModels = false;
        var repoPath = Directory.GetCurrentDirectory();
        var model = DefaultModel;
        string? languageCode = null;
        string? diffFilePath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    showHelp = true;
                    break;
                case "--staged":
                    staged = true;
                    break;
                case "--check":
                    check = true;
                    break;
                case "--list-models":
                    listModels = true;
                    break;
                case "--lang" when i + 1 < args.Length:
                    languageCode = args[++i];
                    break;

                // Value flags consume the next argument. Advance past it, so a
                // value that happens to look like a flag is not read as one.
                case "--diff" when i + 1 < args.Length:
                    diffFilePath = args[++i];
                    break;
                case "--repo" when i + 1 < args.Length:
                    repoPath = args[++i];
                    break;
                case "--model" when i + 1 < args.Length:
                    model = args[++i];
                    break;
            }
        }

        var languageRecognized = ReviewLanguage.TryResolve(languageCode ?? "en", out var language);
        var languageWarning = languageCode is not null && !languageRecognized
            ? $"Warning: unrecognised language code '{languageCode}'; using English."
            : null;

        return new CliOptions
        {
            ShowHelp = showHelp,
            Staged = staged,
            Check = check,
            ListModels = listModels,
            RepoPath = repoPath,
            Model = model,
            Language = language,
            LanguageWarning = languageWarning,
            DiffFilePath = diffFilePath,
        };
    }

    public static string UsageText => """
        groq-pr-reviewer-net — code review powered by an open-weight model on Groq.

        Usage:
          groq-pr-reviewer [options]

        Options:
          --staged         Review staged changes (git diff --staged)
          --diff <file>    Review a .diff/.patch file instead of running git
          --repo <path>    Target repository (default: current directory)
          --model <id>     Groq model id (default: openai/gpt-oss-120b)
          --lang <code>    Review language: en, pt, es, fr, de, ja (default: en)
          --check          Validate the API key setup without calling the API
          --list-models    List the model ids available to your key
          --help, -h       Show this help
        """;
}
