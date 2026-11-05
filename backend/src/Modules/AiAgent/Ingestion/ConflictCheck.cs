using System.Globalization;
using System.Text.RegularExpressions;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.Shipping.Contracts;

namespace Nihasdesk.Modules.AiAgent.Ingestion;

/// <summary>Layer 1 of PRD 4.7: what the shop's settings say, which always wins.</summary>
internal sealed record BusinessFacts(IReadOnlyList<ShippingRate> Zones, int? ReturnDays);

/// <summary>
/// A section says something other than the settings. <see cref="Kind"/>: <c>shipping_cost</c>,
/// <c>delivery_days</c>, <c>return_days</c> or <c>payment_method</c>. Found and Expected are ready to show.
/// </summary>
internal sealed record Conflict(string Kind, Guid SectionId, int? Page, string Found, string Expected);

/// <summary>
/// Finds conflicts with the settings and price lists, by code, not by a model (ADR 0026 §2). It
/// reads word by word near amounts and day counts, so it warns and never decides: some warnings
/// will be wrong and some conflicts will be missed. Excluded sections aren't checked.
/// </summary>
internal static partial class ConflictCheck
{
    /// <summary>This many different amounts in one section: it looks like a price list (PRD 4.7).</summary>
    public const int PriceListAmounts = 4;

    public static List<Conflict> Find(IEnumerable<KnowledgeSection> sections, BusinessFacts facts)
    {
        var conflicts = new List<Conflict>();
        foreach (var section in sections.Where(s => !s.Excluded))
        {
            foreach (var sentence in SentenceEnd().Split(section.Text).Where(s => s.Length > 0))
            {
                // The heading counts: a row of a table under "Shipping" is about shipping.
                var context = $"{section.HeadingPath} {sentence}";
                conflicts.AddRange(ShippingCost(section, sentence, context, facts));
                conflicts.AddRange(DeliveryDays(section, sentence, context, facts));
                conflicts.AddRange(ReturnDays(section, sentence, context, facts));
                conflicts.AddRange(PaymentMethods(section, sentence));
            }
        }

        return conflicts.DistinctBy(c => (c.Kind, c.SectionId, c.Found)).ToList();
    }

    public static List<Guid> PriceLists(IEnumerable<KnowledgeSection> sections) =>
        sections
            .Where(s => !s.Excluded && Guardrails.Amounts(s.Text).Distinct().Count() >= PriceListAmounts)
            .Select(s => s.Id)
            .ToList();

    private static IEnumerable<Conflict> ShippingCost(KnowledgeSection section, string sentence, string context, BusinessFacts facts)
    {
        // "Return shipping €5" is about returns, not the zones.
        if (facts.Zones.Count == 0 || !ShippingWords().IsMatch(context) || ReturnWords().IsMatch(sentence))
        {
            yield break;
        }

        var zones = ZonesIn(sentence, facts.Zones);
        var allowed = zones.SelectMany(z => new[] { (long?)z.Price, z.FreeOver }).OfType<long>().ToHashSet();
        foreach (var amount in Guardrails.Amounts(sentence).Distinct().Where(a => !allowed.Contains(a)))
        {
            yield return new Conflict("shipping_cost", section.Id, section.Page, Money(amount, zones[0].Currency), string.Join(", ", zones.Select(CostOf)));
        }
    }

    private static IEnumerable<Conflict> DeliveryDays(KnowledgeSection section, string sentence, string context, BusinessFacts facts)
    {
        var known = facts.Zones.Where(z => z.MinDays is not null || z.MaxDays is not null).ToList();
        if (known.Count == 0 || !DeliveryWords().IsMatch(context) || ReturnWords().IsMatch(sentence) || RefundWords().IsMatch(sentence))
        {
            yield break;
        }

        var zones = ZonesIn(sentence, known);
        foreach (Match match in Days().Matches(sentence))
        {
            var (from, to) = RangeOf(match);
            if (!zones.Any(z => from >= (z.MinDays ?? z.MaxDays) && to <= (z.MaxDays ?? z.MinDays)))
            {
                yield return new Conflict("delivery_days", section.Id, section.Page, match.Value.Trim(), string.Join(", ", zones.Select(DaysOf)));
            }
        }
    }

    private static IEnumerable<Conflict> ReturnDays(KnowledgeSection section, string sentence, string context, BusinessFacts facts)
    {
        // Refund speed ("refund within 7 days") is not the return window.
        if (facts.ReturnDays is not { } days || !ReturnWords().IsMatch(context) || RefundWords().IsMatch(sentence))
        {
            yield break;
        }

        foreach (Match match in Days().Matches(sentence))
        {
            var (from, to) = RangeOf(match);
            if (from != days || to != days)
            {
                yield return new Conflict("return_days", section.Id, section.Page, match.Value.Trim(), days.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>Phase 1 has only cash on delivery (ADR 0004). "We don't take PayPal" is no conflict.</summary>
    private static IEnumerable<Conflict> PaymentMethods(KnowledgeSection section, string sentence)
    {
        if (Negation().IsMatch(sentence))
        {
            yield break;
        }

        foreach (Match match in OtherPayments().Matches(sentence))
        {
            yield return new Conflict("payment_method", section.Id, section.Page, match.Value, "cash on delivery");
        }
    }

    /// <summary>The zones a sentence names, or all of them when it names none.</summary>
    private static List<ShippingRate> ZonesIn(string sentence, IReadOnlyList<ShippingRate> zones)
    {
        var named = zones
            .Where(z => Regex.IsMatch(sentence, $@"\b{Regex.Escape(z.Name)}\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            .ToList();
        return named.Count > 0 ? named : [.. zones];
    }

    private static (int From, int To) RangeOf(Match match)
    {
        var from = int.Parse(match.Groups["from"].Value, CultureInfo.InvariantCulture);
        return (from, match.Groups["to"].Success ? int.Parse(match.Groups["to"].Value, CultureInfo.InvariantCulture) : from);
    }

    private static string CostOf(ShippingRate zone) =>
        $"{zone.Name}: {Money(zone.Price, zone.Currency)}{(zone.FreeOver is { } free ? $" / {Money(free, zone.Currency)}" : string.Empty)}";

    private static string DaysOf(ShippingRate zone) =>
        $"{zone.Name}: {(zone.MinDays == zone.MaxDays || zone.MinDays is null || zone.MaxDays is null ? (zone.MinDays ?? zone.MaxDays)!.Value.ToString(CultureInfo.InvariantCulture) : $"{zone.MinDays}-{zone.MaxDays}")}";

    private static string Money(long cents, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{cents / 100}.{cents % 100:00} {currency}");

    // A new line ends a sentence too: each row of a table is checked alone.
    [GeneratedRegex(@"(?<=[.!?;])\s+|\n")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\b(?:spedizion\w*|spediamo|spedire|shipping|ship|courier|corriere)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShippingWords();

    [GeneratedRegex(@"\b(?:spedizion\w*|spediamo|spedire|consegn\w*|shipping|ship|deliver\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DeliveryWords();

    [GeneratedRegex(@"\b(?:res[oi]|restitu\w*|recesso|returns?|withdraw\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReturnWords();

    [GeneratedRegex(@"\b(?:rimbors\w*|refunds?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RefundWords();

    [GeneratedRegex(@"(?<from>\d{1,3})(?:\s*(?:-|–|a|to)\s*(?<to>\d{1,3}))?\s*(?:(?:business|working)\s+)?(?:giorn[oi]|gg|days?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Days();

    [GeneratedRegex(@"\b(?:paypal|visa|mastercard|american express|amex|apple pay|google pay|klarna|scalapay|satispay|bonifico|cart[ae] di credito|credit cards?|debit cards?|bank transfer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherPayments();

    [GeneratedRegex(@"\b(?:non|not|no|don't|doesn't|never|mai)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Negation();
}
