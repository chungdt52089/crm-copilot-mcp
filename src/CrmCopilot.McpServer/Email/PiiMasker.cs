using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Pii;

namespace CrmCopilot.McpServer.Email;

/// <summary>
/// P0-07 masking for generate_email (docs/08_RAG_EMAIL_AND_PII_SPEC.md §6). Three distinct
/// mechanisms, applied to <c>objective</c> and every interaction's Summary/Outcome/NextAction:
///
/// 1. Structural exclusion (not a "replace" at all): <see cref="CustomerDto.Email"/>/
///    <see cref="CustomerDto.Phone"/>/<see cref="CustomerDto.AccountReference"/> are never read by
///    this type or any caller of it into a prompt/query string — they are simply not passed in.
///    This alone guarantees these three raw values can never reach Gemini via the "known customer
///    field" path, unconditionally, for every call.
/// 2. Field-based placeholder substitution (the one field actually referenced):
///    <see cref="CustomerDto.FullName"/> is replaced everywhere it appears, verbatim, with
///    <c>{{CUSTOMER_NAME}}</c>.
/// 3. Regex fallback (defense-in-depth, genuinely conditional on scanning the text): email/phone/
///    digit-run/secret-token shaped substrings, whether or not they happen to be this customer's
///    own known values.
///
/// <see cref="MaskedEmailContext.MaskedFieldTypes"/> reflects this split — "name"/"email"/"phone"/
/// "accountReference" are always present (mechanisms 1/2 are unconditional by construction);
/// "secret" is present only when mechanism 3 actually matched something for this call.
/// </summary>
internal static class PiiMasker
{
    private const string NamePlaceholder = "{{CUSTOMER_NAME}}";
    private const string EmailPlaceholder = "[redacted-email]";
    private const string PhonePlaceholder = "[redacted-phone]";
    private const string AccountPlaceholder = "[redacted-account]";
    private const string SecretPlaceholder = "[REDACTED_SECRET]";

    public static MaskedEmailContext Mask(CustomerDto customer, IReadOnlyList<InteractionDto> interactions, string objective)
    {
        var secretDetected = false;

        var (maskedObjective, objectiveHadSecret) = MaskFreeText(objective, customer);
        secretDetected |= objectiveHadSecret;

        var summaries = new List<string>(interactions.Count);
        var evidence = new List<InteractionEvidence>(interactions.Count);

        foreach (var interaction in interactions)
        {
            var (maskedSummary, summaryHadSecret) = MaskFreeText(interaction.Summary, customer);
            var (maskedOutcome, outcomeHadSecret) = MaskFreeText(interaction.Outcome, customer);

            string? maskedNextAction = null;
            var nextActionHadSecret = false;
            if (interaction.NextAction is { } nextAction)
            {
                (maskedNextAction, nextActionHadSecret) = MaskFreeText(nextAction, customer);
            }

            secretDetected |= summaryHadSecret || outcomeHadSecret || nextActionHadSecret;

            summaries.Add(maskedSummary);
            evidence.Add(new InteractionEvidence(
                $"crm:interaction:{interaction.Id}",
                interaction.Type,
                interaction.OccurredAtUtc,
                maskedSummary,
                maskedOutcome,
                maskedNextAction));
        }

        var maskedFieldTypes = new List<string> { "name", "email", "phone", "accountReference" };
        if (secretDetected)
        {
            maskedFieldTypes.Add("secret");
        }

        return new MaskedEmailContext(maskedObjective, summaries, evidence, maskedFieldTypes);
    }

    /// <summary>Applies mechanisms 2 (name placeholder) then 3 (regex fallback, in the exact order
    /// Phone-before-DigitRun that ConversationMessageSanitizer already documents — a phone number
    /// is digit-heavy enough to also match DigitRun, so Phone must be replaced first or DigitRun
    /// would double-redact it as "[redacted-account]"). Returns whether the secret-token pattern
    /// matched, so the caller can decide whether "secret" belongs in MaskedFieldTypes.</summary>
    private static (string Masked, bool SecretDetected) MaskFreeText(string text, CustomerDto customer)
    {
        var masked = text;

        if (!string.IsNullOrEmpty(customer.FullName))
        {
            masked = masked.Replace(customer.FullName, NamePlaceholder, StringComparison.Ordinal);
        }

        masked = PiiPatterns.Email.Replace(masked, EmailPlaceholder);
        masked = PiiPatterns.Phone.Replace(masked, PhonePlaceholder);
        masked = PiiPatterns.DigitRun.Replace(masked, AccountPlaceholder);

        var secretDetected = PiiPatterns.SecretToken.IsMatch(masked);
        masked = PiiPatterns.SecretToken.Replace(masked, SecretPlaceholder);

        return (masked, secretDetected);
    }
}
