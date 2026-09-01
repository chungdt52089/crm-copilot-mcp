using System.Collections.Concurrent;

namespace CrmCopilot.MockCrmApi.Data;

/// <summary>
/// P0-14 (PD-023): the set of customer ids deleted during this process's lifetime. Deliberately
/// RAM-only — nothing here is ever written to data/crm/customers.json, which is locked by
/// SyntheticDatasetGeneratorTests.Generate_Default_MatchesCheckedInDataset and by the SHA-256 in
/// docs/CHECKPOINT_STATUS.md §2. Restarting MockCrmApi therefore restores every customer, and that
/// restore is exactly what proves the delete was soft.
///
/// Registered as a singleton so the set spans the whole process; <see cref="CrmDataset"/> itself is
/// left untouched, keeping its "read-only view over the loaded dataset" contract literally true.
///
/// Ordinal comparison matches CrmDataset's own id dictionary (built by ToDictionary with the
/// default comparer), so a differently-cased id is a miss here exactly as it already is there.
/// </summary>
internal sealed class SoftDeleteRegistry
{
    private readonly ConcurrentDictionary<string, byte> _deleted = new(StringComparer.Ordinal);

    public bool IsDeleted(string customerId) => _deleted.ContainsKey(customerId);

    /// <summary>Records the deletion.</summary>
    /// <returns><c>true</c> if this call performed it; <c>false</c> if the id was already deleted.</returns>
    public bool TryDelete(string customerId) => _deleted.TryAdd(customerId, 0);
}
