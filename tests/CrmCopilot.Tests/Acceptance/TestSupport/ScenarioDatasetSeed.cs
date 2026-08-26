using CrmCopilot.Contracts.Crm;
using CrmCopilot.MockCrmApi.Data;

namespace CrmCopilot.Tests.Acceptance.TestSupport;

/// <summary>
/// Canonical values read from the checked-in synthetic dataset (data/crm/*.json) rather than
/// re-typed as literals, so the acceptance scenarios assert against the same records the demo and
/// the Mock CRM API serve. Reuses <see cref="CrmDatasetLoader.LoadFromAppBaseDirectory"/> — the
/// loader already used by DatasetValidationTests — instead of parsing the JSON a second way.
///
/// docs/03_ACCEPTANCE_CRITERIA.md §7 freezes these values as of P0-02; the guards below turn a
/// silent dataset drift into a loud, named failure rather than a confusing scenario mismatch.
/// </summary>
internal static class ScenarioDatasetSeed
{
    public const string CanonicalCustomerId = "CUS-0001";
    public const string MissingCustomerId = "CUS-9999";
    public const string CanonicalProductCode = "PRD-SAV-006M";
    public const string CanonicalProductSourceId = "kb:product:PRD-SAV-006M";
    public const string CanonicalTemplateSourceId = "kb:email-template:TPL-EMAIL-MATURITY-01";

    private static readonly Lazy<CrmDataset> LazyDataset = new(CrmDatasetLoader.LoadFromAppBaseDirectory);

    public static CrmDataset Dataset => LazyDataset.Value;

    /// <summary>CUS-0001 — "Nguyễn Minh Anh", the demo's canonical customer.</summary>
    public static CustomerDto CanonicalCustomer =>
        Dataset.FindById(CanonicalCustomerId)
        ?? throw new InvalidOperationException($"Dataset drift: {CanonicalCustomerId} is missing from data/crm/customers.json.");

    /// <summary>A full name held by exactly one customer — drives T02.</summary>
    public static string UniqueFullName => CanonicalCustomer.FullName;

    /// <summary>The deliberate duplicate-name pair the generator guarantees — drives T03.</summary>
    public static (string FullName, IReadOnlyList<string> CustomerIds) DuplicateNameGroup
    {
        get
        {
            var group = Dataset.Customers
                .GroupBy(customer => customer.FullName, StringComparer.Ordinal)
                .FirstOrDefault(candidates => candidates.Count() > 1)
                ?? throw new InvalidOperationException(
                    "Dataset drift: data/crm/customers.json no longer contains a duplicated full name, so T03 cannot be evaluated.");

            return (group.Key, group.Select(customer => customer.Id).Order(StringComparer.Ordinal).ToList());
        }
    }

    /// <summary>Every interaction of CUS-0001, newest first — the order T05 asserts.</summary>
    public static IReadOnlyList<InteractionDto> CanonicalInteractionsNewestFirst =>
        Dataset.GetInteractions(CanonicalCustomerId, int.MaxValue);

    public static IReadOnlyList<string> CanonicalInteractionIdsNewestFirst =>
        CanonicalInteractionsNewestFirst.Select(interaction => interaction.Id).ToList();

    public static IReadOnlyList<string> CanonicalInteractionSourceIdsNewestFirst =>
        CanonicalInteractionsNewestFirst.Select(interaction => $"crm:interaction:{interaction.Id}").ToList();

    /// <summary>
    /// The savings interaction the demo's email step is grounded in ("tiền gửi ... 6 tháng").
    /// docs/03 §7 requires it to exist and to be recent; T05 additionally asserts it is the newest.
    /// </summary>
    public static InteractionDto CanonicalSavingsInteraction =>
        CanonicalInteractionsNewestFirst.FirstOrDefault(interaction =>
            interaction.Summary.Contains("tiền gửi", StringComparison.OrdinalIgnoreCase)
            && interaction.Summary.Contains("6 tháng", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"Dataset drift: no interaction of {CanonicalCustomerId} mentions the canonical savings need (docs/03 §7).");

    /// <summary>
    /// The raw PII values that must never reach Gemini or any application log. Read from the
    /// dataset so a regenerated dataset cannot leave the scan checking stale strings.
    /// </summary>
    public static IReadOnlyList<string> CanonicalPiiValues =>
    [
        CanonicalCustomer.FullName,
        CanonicalCustomer.Email,
        CanonicalCustomer.Phone,
        CanonicalCustomer.AccountReference,
    ];
}
