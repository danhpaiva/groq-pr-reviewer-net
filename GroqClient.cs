using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GroqPrReviewer;

/// <summary>Talks to Groq's OpenAI-compatible API.</summary>
internal sealed class GroqClient : IDisposable
{
    private const string ChatCompletionsUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string ModelsUrl = "https://api.groq.com/openai/v1/models";

    private readonly HttpClient _http;

    public GroqClient(string apiKey)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Lists the model ids this key can actually reach, so a deprecated default
    /// is easy to replace via --model without digging through the docs.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListModelIdsAsync()
    {
        // Listing is a cheap call; don't let it hang for the full review timeout.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await _http.GetAsync(ModelsUrl, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeApiError(response.StatusCode, body));

        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Unexpected response from the Groq models endpoint: {body}");

        var ids = new List<string>();
        foreach (var entry in data.EnumerateArray())
        {
            if (entry.TryGetProperty("id", out var id) && id.GetString() is { } value)
                ids.Add(value);
        }

        ids.Sort(StringComparer.Ordinal);
        return ids;
    }

    public async Task<string> ReviewAsync(string model, string diff, ReviewLanguage language)
    {
        var requestBody = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = BuildSystemPrompt(language) },
                new { role = "user", content = $"Review the diff below:\n\n{FenceDiff(diff)}" },
            },
            temperature = 0.2,
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(ChatCompletionsUrl, content);
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

    internal static string BuildSystemPrompt(ReviewLanguage language) => $"""
        You are a senior code reviewer. Analyse the pull request diff below and reply in {language.Name},
        as bullet points organised into these sections:
        - {language.BugsAndCorrectness}
        - {language.Security}
        - {language.Performance}
        - {language.BestPracticesAndReadability}

        Be concise and specific. If a section has nothing worth raising, write "{language.NothingToFlag}".
        Ignore trivial formatting changes.
        """;

    /// <summary>
    /// A diff that touches Markdown files carries its own ``` sequences, which
    /// would close the fence early and hand the model a mangled prompt. Open
    /// with a fence longer than the longest backtick run inside the diff.
    /// </summary>
    internal static string FenceDiff(string diff)
    {
        var longest = 0;
        var current = 0;

        foreach (var c in diff)
        {
            if (c != '`')
            {
                current = 0;
                continue;
            }

            current++;
            if (current > longest)
                longest = current;
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}diff\n{diff}\n{fence}";
    }

    internal static string DescribeApiError(HttpStatusCode status, string body)
    {
        var detail = TryExtractErrorMessage(body) ?? body;

        var hint = status switch
        {
            HttpStatusCode.Unauthorized =>
                "\nGroq rejected the key. Check that GROQ_API_KEY is complete and active at https://console.groq.com/keys " +
                "(run --check to see where it is being read from). Groq keys start with gsk_ — a key starting with xai- " +
                "belongs to xAI/Grok, a different company. Note that an environment variable takes precedence over .env.",
            HttpStatusCode.NotFound =>
                "\nModel not found — it may have been deprecated. Run --list-models to see what your key can reach, then pass --model <id>.",
            HttpStatusCode.TooManyRequests =>
                "\nRate limit reached. Wait a moment and try again.",
            _ => string.Empty,
        };

        return $"Groq API error ({(int)status} {status}): {detail}{hint}";
    }

    private static string? TryExtractErrorMessage(string body)
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

    public void Dispose() => _http.Dispose();
}
