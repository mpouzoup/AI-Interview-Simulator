using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AIInterviewSimulator.Tests")]

namespace AIInterviewSimulator.Engine.Feedback;

// These are evidence states, not grades or claims of semantic understanding.
internal enum CriterionState { Uncertain, Detected }
internal enum EvidenceReason { NoReliableEvidence, MatchedPattern, ExplicitNonAnswer, NegatedEvidence, ConflictingEvidence }
internal sealed record CriterionEvidence(string CriterionId, CriterionState State, EvidenceReason Reason, string? PatternId = null, bool Required = true);
internal sealed record ScriptFeedbackAnalysis(IReadOnlyList<CriterionEvidence> Criteria, string FeedbackId, string Text);

internal static class GreekTextMatcher
{
    internal static string Normalize(string? text)
    {
        var builder = new StringBuilder();
        foreach (var character in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace('ς', 'σ');
    }

    internal static bool Matches(string text, string pattern) => Regex.IsMatch(text,
        pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static CriterionEvidence Evaluate(string id, string text,
        IReadOnlyList<(string Id, string Pattern)> patterns, string denial, bool allowIntent = false)
    {
        // Do not treat quoted examples, questions or hypothetical advice as autobiographical evidence.
        var unquoted = Regex.Replace(text, "«[^»]*»|\"[^\"]*\"|“[^”]*”", " ",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        string? matched = null;
        var negated = false;
        var affirmedExpressions = new HashSet<string>(StringComparer.Ordinal);
        var negatedExpressions = new HashSet<string>(StringComparer.Ordinal);
        var denied = false;
        foreach (var clause in Regex.Split(unquoted, @"(?<=[.!?;;])|[\r\n]+|\bαλλα\b|\bομωσ\b"))
        {
            if (clause.TrimEnd().EndsWith('?') || clause.TrimEnd().EndsWith(';') || clause.TrimEnd().EndsWith(';')) continue;
            // Future intentions are appropriate for goals/motivation, but not evidence of past actions.
            if (Matches(clause, @"\b(αν|εαν|ισωσ)\b")) continue;
            // A retrospective lesson is not generic advice ("έμαθα ότι πρέπει...").
            if (Matches(clause, @"\bπρεπει\b") &&
                !Matches(clause, @"\b(?:εμαθα|καταλαβα|συνειδητοποιησα)\s+οτι\s+πρεπει\b")) continue;
            if (!allowIntent && Matches(clause, @"\b(θα|μπορω|μπορουμε|μπορουσα να)\b")) continue;
            denied |= Matches(clause, denial);
            foreach (var pattern in patterns)
            {
                foreach (Match match in Regex.Matches(clause, pattern.Pattern,
                    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                {
                    var prefix = clause[..match.Index];
                    // Only local negation: a negation in a different sentence must not erase evidence.
                    if (Matches(prefix, @"\b(δεν|μην|μη|ουτε|χωρισ να)\s+(?:\p{L}+\s+){0,2}$"))
                    {
                        negated = true;
                        negatedExpressions.Add(Regex.Replace(match.Value, @"\s+", " "));
                    }
                    else
                    {
                        matched ??= pattern.Id;
                        affirmedExpressions.Add(Regex.Replace(match.Value, @"\s+", " "));
                    }
                }
            }
        }
        if (matched != null && (denied || affirmedExpressions.Overlaps(negatedExpressions)))
            return new(id, CriterionState.Uncertain, EvidenceReason.ConflictingEvidence);
        if (denied) return new(id, CriterionState.Uncertain, EvidenceReason.ExplicitNonAnswer);
        if (matched != null) return new(id, CriterionState.Detected, EvidenceReason.MatchedPattern, matched);
        return new(id, CriterionState.Uncertain,
            negated ? EvidenceReason.NegatedEvidence : EvidenceReason.NoReliableEvidence);
    }
}
