using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

const string GroqApiUrl = "https://api.groq.com/openai/v1/chat/completions";
const string GroqModelsUrl = "https://api.groq.com/openai/v1/models";
// Open-weight (Apache 2.0) and currently the strongest chat model on Groq.
// Run --list-models if this one is ever retired.
const string DefaultModel = "openai/gpt-oss-120b";

// Very large diffs blow past the model's context window.
const int MaxDiffChars = 60_000;

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

var repoPath = Directory.GetCurrentDirectory();
var staged = args.Contains("--staged");
var check = args.Contains("--check");
var listModels = args.Contains("--list-models");
var model = DefaultModel;
string? diffFilePath = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--diff" && i + 1 < args.Length)
        diffFilePath = args[i + 1];
    if (args[i] == "--repo" && i + 1 < args.Length)
        repoPath = args[i + 1];
    if (args[i] == "--model" && i + 1 < args.Length)
        model = args[i + 1];
}

try
{
    var (apiKey, source) = LoadApiKey();

    if (check)
    {
        if (apiKey is null)
        {
            Console.Error.WriteLine("GROQ_API_KEY not found (neither an environment variable nor a .env file).");
            return 1;
        }

        // Diagnostics without leaking the secret: only origin, length and shape.
        Console.WriteLine($"Key source  : {source}");
        Console.WriteLine($"Length      : {apiKey.Length} characters");
        Console.WriteLine($"gsk_ prefix : {(apiKey.StartsWith("gsk_", StringComparison.Ordinal) ? "ok" : "UNEXPECTED — Groq keys start with gsk_. Note that Groq (groq.com) is not xAI/Grok (x.ai).")}");
        Console.WriteLine($"Model       : {model}");
        return 0;
    }

    if (apiKey is null)
    {
        Console.Error.WriteLine("GROQ_API_KEY not found. Set the environment variable or create a .env file (see .env.example).");
        return 1;
    }

    if (listModels)
    {
        foreach (var id in await FetchModelIds(apiKey))
            Console.WriteLine(id);
        return 0;
    }

    var diff = diffFilePath is not null
        ? await File.ReadAllTextAsync(diffFilePath)
        : await RunGitDiff(repoPath, staged);

    if (string.IsNullOrWhiteSpace(diff))
    {
        Console.WriteLine(staged
            ? "No staged changes found (git diff --staged)."
            : "No changes found (git diff). Tip: use --staged to review what is already staged.");
        return 0;
    }

    if (diff.Length > MaxDiffChars)
    {
        Console.Error.WriteLine($"Warning: diff is {diff.Length} characters; sending only the first {MaxDiffChars}.");
        diff = diff[..MaxDiffChars];
    }

    Console.WriteLine($"Sending diff to Groq for review ({model})...\n");

    var review = await RequestReview(apiKey, model, diff);
    Console.WriteLine(review);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        groq-pr-reviewer-net — code review powered by Llama 3.3 70B on Groq.

        Usage:
          groq-pr-reviewer [options]

        Options:
          --staged         Review staged changes (git diff --staged)
          --diff <file>    Review a .diff/.patch file instead of running git
          --repo <path>    Target repository (default: current directory)
          --model <id>     Groq model id (default: openai/gpt-oss-120b)
          --check          Validate the API key setup without calling the API
          --list-models    List the model ids available to your key
          --help, -h       Show this help
        """);
}

static async Task<string> RunGitDiff(string repoPath, bool staged)
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

static (string? Key, string Source) LoadApiKey()
{
    var fromEnv = Environment.GetEnvironmentVariable("GROQ_API_KEY");
    if (!string.IsNullOrWhiteSpace(fromEnv))
        return (fromEnv.Trim(), "GROQ_API_KEY environment variable");

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
        if (parts.Length != 2 || parts[0].Trim() != "GROQ_API_KEY")
            continue;

        var value = ParseEnvValue(parts[1]);
        return string.IsNullOrWhiteSpace(value) ? (null, "none") : (value, envPath);
    }

    return (null, "none");
}

// Walk up from both the current directory and the binary's directory so this
// works with `dotnet run` as well as a published executable.
static string? FindEnvFile()
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

static string ParseEnvValue(string raw)
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

// Lists the model ids the key can actually reach, so a deprecated default is
// easy to replace via --model without digging through the docs.
static async Task<List<string>> FetchModelIds(string apiKey)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    var response = await client.GetAsync(GroqModelsUrl);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException(DescribeApiError(response.StatusCode, body));

    using var doc = JsonDocument.Parse(body);
    var ids = new List<string>();

    if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        throw new InvalidOperationException($"Unexpected response from the Groq models endpoint: {body}");

    foreach (var entry in data.EnumerateArray())
    {
        if (entry.TryGetProperty("id", out var id) && id.GetString() is { } value)
            ids.Add(value);
    }

    ids.Sort(StringComparer.Ordinal);
    return ids;
}

static async Task<string> RequestReview(string apiKey, string model, string diff)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    const string systemPrompt = """
        You are a senior code reviewer. Analyse the pull request diff below and reply in English,
        as bullet points organised into these sections:
        - Bugs and correctness
        - Security
        - Performance
        - Best practices / readability

        Be concise and specific. If a section has nothing worth raising, write "Nothing to flag".
        Ignore trivial formatting changes.
        """;

    var requestBody = new
    {
        model,
        messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = $"Review the diff below:\n\n```diff\n{diff}\n```" },
        },
        temperature = 0.2,
    };

    var json = JsonSerializer.Serialize(requestBody);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");

    var response = await client.PostAsync(GroqApiUrl, content);
    var responseBody = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException(DescribeApiError(response.StatusCode, responseBody));

    using var doc = JsonDocument.Parse(responseBody);
    return doc.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString() ?? "(empty response)";
}

static string DescribeApiError(System.Net.HttpStatusCode status, string body)
{
    var detail = TryExtractErrorMessage(body) ?? body;

    var hint = status switch
    {
        System.Net.HttpStatusCode.Unauthorized =>
            "\nGroq rejected the key. Check that GROQ_API_KEY is complete and active at https://console.groq.com/keys " +
            "(run --check to see where it is being read from). Groq keys start with gsk_ — a key starting with xai- " +
            "belongs to xAI/Grok, a different company. Note that an environment variable takes precedence over .env.",
        System.Net.HttpStatusCode.NotFound =>
            "\nModel not found — it may have been deprecated. See the active models at https://console.groq.com/docs/models and pass --model <id>.",
        System.Net.HttpStatusCode.TooManyRequests =>
            "\nRate limit reached. Wait a moment and try again.",
        _ => string.Empty,
    };

    return $"Groq API error ({(int)status} {status}): {detail}{hint}";
}

static string? TryExtractErrorMessage(string body)
{
    try
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("error", out var error) &&
            error.TryGetProperty("message", out var message))
            return message.GetString();
    }
    catch (JsonException)
    {
        // non-JSON body: fall back to the raw text
    }

    return null;
}
