using GroqPrReviewer;

namespace GroqPrReviewer.Tests;

public sealed class ReviewLanguageTests
{
    [Theory]
    [InlineData("en", "English", "Bugs and correctness", "Security", "Performance", "Best practices / readability", "Nothing to flag")]
    [InlineData("pt", "Portuguese", "Erros e correção", "Segurança", "Desempenho", "Boas práticas / legibilidade", "Nada a apontar")]
    [InlineData("es", "Spanish", "Errores y corrección", "Seguridad", "Rendimiento", "Buenas prácticas / legibilidad", "Nada que señalar")]
    [InlineData("fr", "French", "Bugs et exactitude", "Sécurité", "Performances", "Bonnes pratiques / lisibilité", "Rien à signaler")]
    [InlineData("de", "German", "Fehler und Korrektheit", "Sicherheit", "Leistung", "Bewährte Praktiken / Lesbarkeit", "Nichts zu beanstanden")]
    [InlineData("ja", "Japanese", "バグと正確性", "セキュリティ", "パフォーマンス", "ベストプラクティス / 可読性", "指摘事項なし")]
    public void TryResolve_returns_the_requested_language(
        string code,
        string expectedName,
        string expectedHeading,
        string expectedSecurity,
        string expectedPerformance,
        string expectedBestPractices,
        string expectedEmptySection)
    {
        var recognized = ReviewLanguage.TryResolve(code, out var language);

        Assert.True(recognized);
        Assert.Equal(expectedName, language.Name);
        Assert.Equal(expectedHeading, language.BugsAndCorrectness);
        Assert.Equal(expectedSecurity, language.Security);
        Assert.Equal(expectedPerformance, language.Performance);
        Assert.Equal(expectedBestPractices, language.BestPracticesAndReadability);
        Assert.Equal(expectedEmptySection, language.NothingToFlag);
    }

    [Theory]
    [InlineData("ES")]
    [InlineData(" es ")]
    public void TryResolve_accepts_case_and_surrounding_whitespace(string code)
    {
        var recognized = ReviewLanguage.TryResolve(code, out var language);

        Assert.True(recognized);
        Assert.Equal("Spanish", language.Name);
    }

    [Fact]
    public void Parse_uses_English_by_default_without_a_warning()
    {
        var options = CliOptions.Parse([]);

        Assert.Equal("English", options.Language.Name);
        Assert.Null(options.LanguageWarning);
    }

    [Fact]
    public void Parse_warns_and_uses_English_for_an_unknown_code()
    {
        var options = CliOptions.Parse(["--lang", "xx"]);

        Assert.Equal("English", options.Language.Name);
        Assert.Equal("Warning: unrecognised language code 'xx'; using English.", options.LanguageWarning);
    }

    [Fact]
    public void System_prompt_uses_localized_language_headings_and_empty_section_text()
    {
        ReviewLanguage.TryResolve("es", out var language);

        var prompt = GroqClient.BuildSystemPrompt(language);

        Assert.Contains("reply in Spanish", prompt);
        Assert.Contains("- Errores y corrección", prompt);
        Assert.Contains("- Seguridad", prompt);
        Assert.Contains("- Rendimiento", prompt);
        Assert.Contains("- Buenas prácticas / legibilidad", prompt);
        Assert.Contains("write \"Nada que señalar\"", prompt);
    }
}
