using System.Text.RegularExpressions;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>
/// Which language a text is in, by common words, for the reply-language rule (Issue #63). Prompt
/// rules alone left Italian sentences in replies to English customers. Only English and Italian for
/// now; anything else (or too little to tell) is null, and the prompt's own rule still applies.
/// </summary>
internal static partial class ReplyLanguage
{
    public const string English = "en";

    public const string Italian = "it";

    // Short messages can show only one such word ("Thanks", "Quanto costa?").
    private const int ShortMessageWords = 5;

    // Words shared by both languages ("in", "no", "a", "me", "come", "i") say nothing and are left out.
    private static readonly HashSet<string> EnglishWords = new(StringComparer.Ordinal)
    {
        "about", "an", "and", "are", "at", "be", "by", "can", "could", "did", "do", "does", "for", "from", "has", "have", "hello", "how", "if", "into", "is", "it", "like", "one", "two",
        "my", "need", "not", "of", "on", "or", "our", "please", "should", "thank", "thanks", "that", "the", "there", "they", "this", "to", "want", "was", "we",
        "what", "when", "where", "which", "will", "with", "would", "yes", "you", "your",
    };

    private static readonly HashSet<string> ItalianWords = new(StringComparer.Ordinal)
    {
        "al", "alla", "anche", "avete", "buongiorno", "che", "ciao", "con", "confermi", "cosa", "così", "dei", "del", "della", "delle", "di", "dove",
        "è", "fate", "gli", "grazie", "hai", "ho", "il", "la", "le", "lo", "ma", "mi", "nel", "non", "per", "perché", "più", "posso", "procedo", "puoi",
        "qualcosa", "quale", "quali", "quando", "quanti", "quanto", "questo", "se", "sei", "si", "sì", "sono", "ti", "tuo", "un", "una", "vorrei", "vuoi",
    };

    /// <summary><see cref="English"/>, <see cref="Italian"/>, or null when the text doesn't clearly show which.</summary>
    public static string? Detect(string? text)
    {
        var (english, italian, words) = Count(text);
        var winner = Math.Max(english, italian);
        var loser = Math.Min(english, italian);
        var clear = winner >= 2 ? winner >= loser * 2 : winner == 1 && loser == 0 && words <= ShortMessageWords;
        return !clear ? null : english > italian ? English : Italian;
    }

    /// <summary>The customer's newest message that shows a language: one with only an address or "ok" doesn't.</summary>
    public static string? OfCustomer(IEnumerable<AiHistoryMessage> history) =>
        history.Reverse().Where(m => m.Author == "customer").Select(m => Detect(m.Text)).FirstOrDefault(language => language is not null);

    /// <summary>Does the reply to a customer in <paramref name="language"/> have a sentence in the other language? A product name alone doesn't count.</summary>
    public static bool HasOtherLanguageSentence(string? reply, string language) =>
        !string.IsNullOrEmpty(reply) && Sentence().Split(reply).Any(sentence =>
        {
            var (english, italian, _) = Count(sentence);
            var (other, same) = language == English ? (italian, english) : (english, italian);
            return other >= 2 && other > same;
        });

    public static string Name(string language) => language == English ? "English" : "Italian";

    private static (int English, int Italian, int Words) Count(string? text)
    {
        int english = 0, italian = 0, words = 0;
        foreach (Match word in Word().Matches(text ?? string.Empty))
        {
            var lower = word.Value.ToLowerInvariant();
            words++;
            english += EnglishWords.Contains(lower) ? 1 : 0;
            italian += ItalianWords.Contains(lower) ? 1 : 0;
        }

        return (english, italian, words);
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();

    [GeneratedRegex(@"[.!?\n]+")]
    private static partial Regex Sentence();
}
