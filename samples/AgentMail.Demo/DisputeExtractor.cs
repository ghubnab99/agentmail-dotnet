using System.Globalization;
using System.Text.RegularExpressions;

namespace AgentMail.Demo;

public sealed record DisputeDetails(string Issue, string? Amount, string? Merchant, string? TransactionDate);

/// <summary>
/// Turns an inbound email into structured case fields. The demo uses deterministic rules so every
/// recording behaves the same; an LLM-backed implementation (e.g. Microsoft.Extensions.AI) can replace it.
/// </summary>
public interface ICaseExtractor
{
    string Method { get; }

    DisputeDetails Extract(string? subject, string? body);
}

public sealed partial class RulesBasedDisputeExtractor : ICaseExtractor
{
    public string Method => "rules-based";

    public DisputeDetails Extract(string? subject, string? body)
    {
        var text = $"{subject}\n{body}";

        var issue = DuplicateCharge().IsMatch(text) ? "Possible duplicate charge"
            : Unrecognized().IsMatch(text) ? "Unrecognized transaction"
            : Refund().IsMatch(text) ? "Refund not received"
            : "Payment enquiry";

        return new DisputeDetails(
            issue,
            Amount().Match(text) is { Success: true } a ? a.Value.Replace(" ", "") : null,
            Merchant().Match(text) is { Success: true } m ? m.Groups["merchant"].Value.Trim() : null,
            ParseDate(Date().Match(text)));
    }

    private static string? ParseDate(Match match)
    {
        if (!match.Success)
        {
            return null;
        }

        var raw = OrdinalSuffix().Replace(match.Groups["date"].Value, "$1").Replace(".", "").Replace(",", "");
        raw = raw.Replace("Sept ", "Sep ", StringComparison.OrdinalIgnoreCase);
        string[] formats = ["MMM d yyyy", "MMMM d yyyy", "MMM d", "MMMM d", "d MMM yyyy", "d MMMM yyyy", "d MMM", "d MMMM"];

        return DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
            ? date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : match.Groups["date"].Value;
    }

    [GeneratedRegex(@"charged twice|double[- ]charged|duplicate (charge|payment|transaction)|billed twice", RegexOptions.IgnoreCase)]
    private static partial Regex DuplicateCharge();

    [GeneratedRegex(@"(don't|do not|didn't|did not) recogni[sz]e|unauthori[sz]ed|fraud", RegexOptions.IgnoreCase)]
    private static partial Regex Unrecognized();

    [GeneratedRegex(@"refund", RegexOptions.IgnoreCase)]
    private static partial Regex Refund();

    [GeneratedRegex(@"[$€£]\s?\d{1,3}(,?\d{3})*(\.\d{2})?")]
    private static partial Regex Amount();

    // "at Acme Store on ..." / "from Acme Store." / "at Acme Store,"
    [GeneratedRegex(@"\b(?:at|from)\s+(?<merchant>[A-Z][\w&'.-]*(?:\s+[A-Z][\w&'.-]*){0,3})")]
    private static partial Regex Merchant();

    [GeneratedRegex(@"\bon\s+(?<date>(?:[A-Z][a-z]{2,8}\.?\s+\d{1,2}(?:st|nd|rd|th)?(?:,?\s+\d{4})?)|(?:\d{1,2}(?:st|nd|rd|th)?\s+[A-Z][a-z]{2,8}(?:\s+\d{4})?))")]
    private static partial Regex Date();

    [GeneratedRegex(@"(\d)(st|nd|rd|th)")]
    private static partial Regex OrdinalSuffix();
}
