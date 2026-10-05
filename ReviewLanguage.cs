namespace GroqPrReviewer;

internal sealed record ReviewLanguage(
    string Name,
    string BugsAndCorrectness,
    string Security,
    string Performance,
    string BestPracticesAndReadability,
    string NothingToFlag)
{
    public static ReviewLanguage English { get; } = new(
        "English",
        "Bugs and correctness",
        "Security",
        "Performance",
        "Best practices / readability",
        "Nothing to flag");

    public static bool TryResolve(string code, out ReviewLanguage language)
    {
        switch (code.Trim().ToLowerInvariant())
        {
            case "en":
                language = English;
                return true;
            case "pt":
                language = new ReviewLanguage(
                    "Portuguese",
                    "Erros e correção",
                    "Segurança",
                    "Desempenho",
                    "Boas práticas / legibilidade",
                    "Nada a apontar");
                return true;
            case "es":
                language = new ReviewLanguage(
                    "Spanish",
                    "Errores y corrección",
                    "Seguridad",
                    "Rendimiento",
                    "Buenas prácticas / legibilidad",
                    "Nada que señalar");
                return true;
            case "fr":
                language = new ReviewLanguage(
                    "French",
                    "Bugs et exactitude",
                    "Sécurité",
                    "Performances",
                    "Bonnes pratiques / lisibilité",
                    "Rien à signaler");
                return true;
            case "de":
                language = new ReviewLanguage(
                    "German",
                    "Fehler und Korrektheit",
                    "Sicherheit",
                    "Leistung",
                    "Bewährte Praktiken / Lesbarkeit",
                    "Nichts zu beanstanden");
                return true;
            case "ja":
                language = new ReviewLanguage(
                    "Japanese",
                    "バグと正確性",
                    "セキュリティ",
                    "パフォーマンス",
                    "ベストプラクティス / 可読性",
                    "指摘事項なし");
                return true;
            default:
                language = English;
                return false;
        }
    }
}
