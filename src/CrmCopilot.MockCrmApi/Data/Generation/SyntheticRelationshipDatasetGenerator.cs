using CrmCopilot.Contracts.Crm;

namespace CrmCopilot.MockCrmApi.Data.Generation;

/// <summary>
/// Deterministic synthetic Opportunity/Campaign generator (P0-10).
///
/// Deliberately a SEPARATE type from <see cref="SyntheticDatasetGenerator"/> and deliberately
/// free of any <see cref="Random"/> at all (plan D9). SyntheticDatasetGenerator consumes a single
/// seeded Random sequentially, so inserting any new Next() call into it — even at the end — is a
/// standing hazard for whoever edits it next: the checked-in customers.json/interactions.json and
/// their SHA-256 hashes in docs/CHECKPOINT_STATUS.md §2 would silently change. Placing this data in
/// its own type with a fixed table removes that coupling by construction rather than by discipline.
///
/// Everything below is anchored to customer indices 1-4, which SyntheticDatasetGenerator guarantees
/// exist (it requires CustomerCount >= 4) and pins by hand; entries for indices 5+ are emitted only
/// when that customer actually exists, so a smaller --customers N still produces a valid dataset.
/// </summary>
internal static class SyntheticRelationshipDatasetGenerator
{
    private const int MaxOpportunities = 8;

    private static readonly DateTime BaseDate = new(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Products for indices 5+, cycled by index — deterministic, no Random.</summary>
    private static readonly string[] RotationProductCodes =
        ["PRD-LOAN-PERSONAL-01", "PRD-INS-LIFE-01", "PRD-SAV-012M", "PRD-LOAN-HOME-01"];

    private static readonly string[] RotationStages = ["Qualification", "Proposal", "Negotiation", "Discovery"];

    private static readonly string[] RotationStatuses =
        [OpportunityStatuses.Open, OpportunityStatuses.Lost, OpportunityStatuses.Open, OpportunityStatuses.Closed];

    public static (IReadOnlyList<OpportunityDto> Opportunities, IReadOnlyList<CampaignDto> Campaigns) Generate(
        DatasetGenerationOptions options, IReadOnlyList<CustomerDto> customers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(customers);

        var existingIds = customers.Select(customer => customer.Id).ToHashSet(StringComparer.Ordinal);

        return (BuildOpportunities(options, existingIds), BuildCampaigns(existingIds));
    }

    private static List<OpportunityDto> BuildOpportunities(DatasetGenerationOptions options, HashSet<string> existingIds)
    {
        var opportunities = new List<OpportunityDto>
        {
            // CUS-0001, entry 1 of 2. Deliberately Won AND carrying the EARLIER
            // ExpectedCloseDateUtc, so it sorts ahead of the Open savings opportunity below under
            // the contract ordering "ExpectedCloseDateUtc ASC, Id ASC". That makes the checked-in
            // dataset itself exercise the filter-before-limit rule (plan Amendment A1): a
            // status=Open, limit=1 request that wrongly applied the limit first would take this
            // record, filter it away, and return nothing.
            new(
                Id: FormatOpportunityId(1),
                CustomerId: FormatCustomerId(1),
                ProductCode: "PRD-CARD-CASHBACK-01",
                Stage: "Closed Won",
                AmountVnd: 80_000_000,
                ExpectedCloseDateUtc: BaseDate.AddDays(-45),
                Status: OpportunityStatuses.Won,
                Synthetic: true),

            // CUS-0001, entry 2 of 2 — the canonical demo opportunity (docs/06 §3 savings
            // scenario). This is the record every "cơ hội đang mở" and short-sentence call-script
            // demo is expected to select.
            new(
                Id: FormatOpportunityId(2),
                CustomerId: FormatCustomerId(1),
                ProductCode: "PRD-SAV-006M",
                Stage: "Proposal",
                AmountVnd: 250_000_000,
                ExpectedCloseDateUtc: BaseDate.AddDays(26),
                Status: OpportunityStatuses.Open,
                Synthetic: true),

            new(
                Id: FormatOpportunityId(3),
                CustomerId: FormatCustomerId(2),
                ProductCode: "PRD-LOAN-PERSONAL-01",
                Stage: "Qualification",
                AmountVnd: 420_000_000,
                ExpectedCloseDateUtc: BaseDate.AddDays(40),
                Status: OpportunityStatuses.Open,
                Synthetic: true),

            new(
                Id: FormatOpportunityId(4),
                CustomerId: FormatCustomerId(3),
                ProductCode: "PRD-INS-LIFE-01",
                Stage: "Discovery",
                AmountVnd: 60_000_000,
                ExpectedCloseDateUtc: BaseDate.AddDays(55),
                Status: OpportunityStatuses.Open,
                Synthetic: true),

            // CUS-0004 deliberately gets NO opportunity at all. It is already the zero-interaction
            // customer, and it is the fixture for the call-script periodic-care fallback: with no
            // Open opportunity the tool must derive a customer_follow_up objective rather than
            // invent one (plan Amendment A6 step 3).
        };

        var sequence = 5;
        for (var index = 5; index <= options.CustomerCount && sequence <= MaxOpportunities; index++)
        {
            var customerId = FormatCustomerId(index);
            if (!existingIds.Contains(customerId))
            {
                continue;
            }

            var rotation = (index - 5) % RotationProductCodes.Length;
            opportunities.Add(new OpportunityDto(
                Id: FormatOpportunityId(sequence),
                CustomerId: customerId,
                ProductCode: RotationProductCodes[rotation],
                Stage: RotationStages[rotation],
                AmountVnd: 100_000_000 + (index * 25_000_000L),
                ExpectedCloseDateUtc: BaseDate.AddDays(10 + (index * 7)),
                Status: RotationStatuses[rotation],
                Synthetic: true));
            sequence++;
        }

        return opportunities;
    }

    private static List<CampaignDto> BuildCampaigns(HashSet<string> existingIds)
    {
        // CUS-0001 is eligible for exactly two of the three campaigns. The third deliberately
        // excludes it, so a demo asking which campaigns CUS-0001 belongs to that wrongly returned
        // the whole campaign list would be visibly wrong rather than coincidentally right (D10).
        return
        [
            new CampaignDto(
                Id: FormatCampaignId(1),
                Name: "Ưu đãi tiền gửi mùa thu 2026",
                Objective: "Giới thiệu sản phẩm tiền gửi kỳ hạn tới khách hàng ưu tiên.",
                TargetSegment: "Priority",
                ProductCodes: ["PRD-SAV-006M", "PRD-SAV-012M"],
                EligibleCustomerIds: KeepExisting(existingIds, 1, 2, 5),
                StartDateUtc: BaseDate.AddDays(-20),
                EndDateUtc: BaseDate.AddDays(45),
                Status: "Active",
                Synthetic: true),

            new CampaignDto(
                Id: FormatCampaignId(2),
                Name: "Giới thiệu thẻ hoàn tiền",
                Objective: "Mở rộng tệp khách hàng dùng thẻ tín dụng hoàn tiền.",
                TargetSegment: "Priority",
                ProductCodes: ["PRD-CARD-CASHBACK-01"],
                EligibleCustomerIds: KeepExisting(existingIds, 1, 3),
                StartDateUtc: BaseDate.AddDays(10),
                EndDateUtc: BaseDate.AddDays(70),
                Status: "Planned",
                Synthetic: true),

            new CampaignDto(
                Id: FormatCampaignId(3),
                Name: "Chăm sóc khách hàng vay",
                Objective: "Theo dõi và hỗ trợ khách hàng đang có khoản vay.",
                TargetSegment: "Standard",
                ProductCodes: ["PRD-LOAN-PERSONAL-01", "PRD-LOAN-HOME-01"],
                EligibleCustomerIds: KeepExisting(existingIds, 2, 4, 6),
                StartDateUtc: BaseDate.AddDays(-5),
                EndDateUtc: BaseDate.AddDays(60),
                Status: "Active",
                Synthetic: true),
        ];
    }

    private static List<string> KeepExisting(HashSet<string> existingIds, params int[] customerIndices) =>
        [.. customerIndices.Select(FormatCustomerId).Where(existingIds.Contains)];

    private static string FormatCustomerId(int index) => $"CUS-{index:0000}";

    private static string FormatOpportunityId(int index) => $"OPP-{index:0000}";

    private static string FormatCampaignId(int index) => $"CMP-{index:0000}";
}
