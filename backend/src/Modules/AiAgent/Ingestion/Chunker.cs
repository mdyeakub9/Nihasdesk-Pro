using System.Text;
using System.Text.RegularExpressions;
using Nihasdesk.Modules.AiAgent.Domain;

namespace Nihasdesk.Modules.AiAgent.Ingestion;

/// <summary>A piece small enough to find and to give the AI, with where it came from.</summary>
internal sealed record Chunk(string? HeadingPath, int? Page, SectionKind Kind, string Text, int Tokens);

/// <summary>
/// Splits a document for search (ADR 0026 §2): follows headings, about 300–500 tokens, some overlap
/// only inside long text. A table, a list or a question with its answer is never split, unless it
/// alone is over the section's size limit, and then only between its lines.
/// </summary>
internal static partial class Chunker
{
    public const int TargetTokens = 500;
    public const int OverlapTokens = 50;
    private const char HeadingSeparator = '›';

    /// <summary>A close estimate for Italian and English, without a tokenizer dependency.</summary>
    public static int Tokens(string text) => Math.Max(1, text.Length / 4);

    public static List<Chunk> Split(IReadOnlyList<DocumentSection> sections)
    {
        var chunks = new List<Chunk>();
        foreach (var section in sections)
        {
            var path = section.Headings.Count == 0 ? null : Shorten(string.Join($" {HeadingSeparator} ", section.Headings), KnowledgeSection.HeadingMaxLength);
            var current = new List<DocumentBlock>();
            var tokens = 0;

            void Flush()
            {
                if (current.Count > 0)
                {
                    var text = string.Join("\n\n", current.Select(b => b.Text));
                    chunks.Add(new Chunk(path, section.Page, SectionKind.Text, text, Tokens(text)));
                    current = [];
                    tokens = 0;
                }
            }

            foreach (var block in PairQuestions(section.Blocks))
            {
                var size = Tokens(block.Text);
                // A table, a list or a FAQ pair is its own section, so it is found and shown whole.
                if (block.Kind != SectionKind.Text || size > TargetTokens)
                {
                    Flush();
                    foreach (var part in block.Kind == SectionKind.Text ? SplitText(block.Text) : SplitLines(block.Text))
                    {
                        chunks.Add(new Chunk(path, section.Page, block.Kind, part, Tokens(part)));
                    }

                    continue;
                }

                if (tokens + size > TargetTokens)
                {
                    Flush();
                }

                current.Add(block);
                tokens += size;
            }

            Flush();
        }

        return chunks;
    }

    /// <summary>A short paragraph ending in "?" and the one after it are one FAQ pair.</summary>
    private static IEnumerable<DocumentBlock> PairQuestions(IReadOnlyList<DocumentBlock> blocks)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block.Kind == SectionKind.Text && block.Text.Length <= 300 && block.Text.EndsWith('?')
                && i + 1 < blocks.Count && blocks[i + 1].Kind == SectionKind.Text)
            {
                yield return new DocumentBlock(SectionKind.Faq, block.Text + "\n" + blocks[++i].Text);
            }
            else
            {
                yield return block;
            }
        }
    }

    /// <summary>Long text: by sentence, with the last sentences repeated at the start of the next part.</summary>
    private static List<string> SplitText(string text)
    {
        var parts = new List<string>();
        var current = new List<string>();
        foreach (var sentence in Sentences(text))
        {
            if (current.Count > 0 && Tokens(string.Join(' ', current.Append(sentence))) > TargetTokens)
            {
                parts.Add(string.Join(' ', current));
                var overlap = new List<string>();
                var kept = 0;
                for (var i = current.Count - 1; i >= 0 && kept + Tokens(current[i]) <= OverlapTokens; i--)
                {
                    overlap.Insert(0, current[i]);
                    kept += Tokens(current[i]);
                }

                // Overlap only when the next sentence still fits after it.
                current = Tokens(string.Join(' ', overlap.Append(sentence))) > TargetTokens ? [] : overlap;
            }

            current.Add(sentence);
        }

        if (current.Count > 0)
        {
            parts.Add(string.Join(' ', current));
        }

        return parts;
    }

    /// <summary>A sentence over the limit (no punctuation) is cut by length, so nothing is lost.</summary>
    private static IEnumerable<string> Sentences(string text)
    {
        var maxChars = TargetTokens * 4;
        foreach (var sentence in SentenceEnd().Split(text).Where(s => s.Length > 0))
        {
            for (var at = 0; at < sentence.Length; at += maxChars)
            {
                yield return sentence.Substring(at, Math.Min(maxChars, sentence.Length - at));
            }
        }
    }

    /// <summary>A list or table stays whole up to the section's size limit; beyond it, between lines.</summary>
    private static List<string> SplitLines(string text)
    {
        if (text.Length <= KnowledgeSection.TextMaxLength)
        {
            return [text];
        }

        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (current.Length > 0 && current.Length + line.Length + 1 > KnowledgeSection.TextMaxLength)
            {
                parts.Add(current.ToString());
                current.Clear();
            }

            current.Append(current.Length > 0 ? "\n" : string.Empty).Append(Shorten(line, KnowledgeSection.TextMaxLength));
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max];

    [GeneratedRegex(@"(?<=[.!?;:])\s+")]
    private static partial Regex SentenceEnd();
}

/// <summary>Italian, English or unknown, for choosing the full-text configuration. No model involved.</summary>
internal static partial class LanguageGuess
{
    private static readonly HashSet<string> Italian = new(StringComparer.Ordinal)
    {
        "il", "lo", "la", "le", "gli", "di", "del", "della", "che", "per", "con", "non", "una", "uno", "sono", "è", "dei", "delle", "nel", "alla", "entro", "gratuito",
    };

    private static readonly HashSet<string> English = new(StringComparer.Ordinal)
    {
        "the", "of", "and", "to", "in", "is", "for", "with", "that", "are", "you", "your", "from", "this", "not", "can", "our", "free", "within",
    };

    public static string? Of(string text, string? hint)
    {
        int it = 0, en = 0;
        foreach (Match word in Word().Matches(text.ToLowerInvariant()))
        {
            it += Italian.Contains(word.Value) ? 1 : 0;
            en += English.Contains(word.Value) ? 1 : 0;
        }

        if (it + en >= 3 && it != en)
        {
            return it > en ? "it" : "en";
        }

        var code = hint is { Length: >= 2 } ? hint[..2].ToLowerInvariant() : null;
        return code is "it" or "en" ? code : null;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
