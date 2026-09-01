using CrmCopilot.Contracts.Crm;
using CrmCopilot.MockCrmApi.Search;

namespace CrmCopilot.MockCrmApi.Data.Generation;

/// <summary>
/// Deterministic synthetic Customer/Interaction dataset generator. Requires CustomerCount >= 4
/// to place the canonical customer, the deliberate duplicate-name pair, and the deliberate
/// zero-interaction customer described in docs/06_DATA_AND_MOCK_API_SPEC.md §2-3.
/// </summary>
internal static class SyntheticDatasetGenerator
{
    private const string Language = "vi";

    private static readonly string[] FamilyNames =
        ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ"];

    private static readonly string[] GivenNames =
    [
        "Minh Anh", "Thị Hương", "Văn Long", "Thị Lan", "Minh Tuấn", "Thị Mai",
        "Văn Đức", "Thị Hoa", "Minh Khôi", "Thị Ngọc", "Văn Hùng", "Thị Thu",
        "Gia Bảo", "Thị Yến", "Đình Phong", "Thị Diễm", "Quang Huy", "Thị Trang",
        "Bảo Ngọc", "Thị Thảo",
    ];

    private static readonly string[] Cities =
        ["Hà Nội", "TP. Hồ Chí Minh", "Đà Nẵng", "Hải Phòng", "Cần Thơ", "Nha Trang", "Huế", "Vũng Tàu"];

    private static readonly string[] RelationshipManagerIds =
        ["RM-001", "RM-002", "RM-003", "RM-004", "RM-005"];

    private static readonly string[] Segments = ["Priority", "Standard"];
    private static readonly string[] Statuses = ["Active", "Active", "Active", "Inactive"];

    private static readonly string[] InteractionTypes = ["Call", "Email", "Meeting", "Note"];
    private static readonly string[] InteractionOutcomes = ["FollowUpRequired", "Completed", "NoResponse"];

    private static readonly string[] InteractionSummaries =
    [
        "Khách hàng hỏi về lãi suất và điều kiện sản phẩm hiện tại.",
        "Trao đổi về nhu cầu mở rộng hạn mức thẻ tín dụng.",
        "Khách hàng phản hồi tích cực về dịch vụ chăm sóc gần đây.",
        "Nhắc lịch thanh toán khoản vay sắp đến hạn.",
        "Khách hàng quan tâm sản phẩm bảo hiểm đi kèm tài khoản.",
        "Cập nhật thông tin liên hệ mới của khách hàng.",
    ];

    private static readonly DateTime BaseDate = new(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);

    public static (IReadOnlyList<CustomerDto> Customers, IReadOnlyList<InteractionDto> Interactions) Generate(
        DatasetGenerationOptions options)
    {
        if (options.CustomerCount < 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.CustomerCount, "CustomerCount must be at least 4.");
        }

        var random = new Random(options.Seed);
        var customers = new List<CustomerDto>(options.CustomerCount);
        var usedNormalizedNames = new HashSet<string>();

        // Index 1: canonical scenario customer (docs/06 §3) — every field fixed, not generated.
        customers.Add(new CustomerDto(
            Id: FormatCustomerId(1),
            FullName: "Nguyễn Minh Anh",
            Email: "minh.anh@example.test",
            Phone: "0900000001",
            AccountReference: "000000000001",
            Segment: "Priority",
            City: "Hà Nội",
            PreferredLanguage: Language,
            RelationshipManagerId: "RM-001",
            Status: "Active",
            Synthetic: true,
            UpdatedAtUtc: BaseDate));
        usedNormalizedNames.Add(CustomerNameNormalizer.Normalize("Nguyễn Minh Anh"));

        // Index 2 and 3: deliberate shared full name, to always exercise the ambiguous-match
        // branch — placed by design, not left to seed luck.
        const string duplicateFullName = "Trần Thị Hương";
        customers.Add(BuildCustomer(2, duplicateFullName, "Đà Nẵng", "Priority", "RM-002", "Active"));
        customers.Add(BuildCustomer(3, duplicateFullName, "Hải Phòng", "Standard", "RM-003", "Active"));
        usedNormalizedNames.Add(CustomerNameNormalizer.Normalize(duplicateFullName));

        // Index 4: deliberate zero-interaction customer (profile itself is unremarkable).
        for (var index = 4; index <= options.CustomerCount; index++)
        {
            string fullName;
            do
            {
                var family = FamilyNames[random.Next(FamilyNames.Length)];
                var given = GivenNames[random.Next(GivenNames.Length)];
                fullName = $"{family} {given}";
            }
            while (!usedNormalizedNames.Add(CustomerNameNormalizer.Normalize(fullName)));

            var city = Cities[random.Next(Cities.Length)];
            var segment = Segments[random.Next(Segments.Length)];
            var relationshipManagerId = RelationshipManagerIds[random.Next(RelationshipManagerIds.Length)];
            var status = Statuses[random.Next(Statuses.Length)];

            customers.Add(BuildCustomer(index, fullName, city, segment, relationshipManagerId, status));
        }

        var interactions = new List<InteractionDto>();
        var interactionSequence = 1;

        // CUS-0001: 3 interactions; the newest one is the canonical savings-follow-up scenario.
        interactions.Add(new InteractionDto(
            Id: FormatInteractionId(interactionSequence++),
            CustomerId: FormatCustomerId(1),
            Type: "Call",
            OccurredAtUtc: BaseDate.AddHours(-0.5),
            Summary: "Khách hàng quan tâm tiền gửi kỳ hạn 6 tháng và ưu tiên rủi ro thấp.",
            Outcome: "FollowUpRequired",
            NextAction: "Gửi thông tin sản phẩm trước ngày 2026-08-20",
            Synthetic: true));
        interactions.Add(GenerateInteraction(ref interactionSequence, 1, BaseDate.AddDays(-10), random));
        interactions.Add(GenerateInteraction(ref interactionSequence, 1, BaseDate.AddDays(-30), random));

        // CUS-0002 / CUS-0003: 2 interactions each.
        for (var index = 2; index <= 3; index++)
        {
            interactions.Add(GenerateInteraction(ref interactionSequence, index, BaseDate.AddDays(-5 * index), random));
            interactions.Add(GenerateInteraction(ref interactionSequence, index, BaseDate.AddDays(-5 * index - 20), random));
        }

        // CUS-0004: intentionally zero interactions — no entries added.

        // Remaining customers: 2 interactions each, 3 for every third one, for a checked-in
        // total of ~25-30 interactions at the default CustomerCount=12 (docs/06 §2).
        for (var index = 5; index <= options.CustomerCount; index++)
        {
            var count = index % 3 == 0 ? 3 : 2;
            for (var occurrence = 0; occurrence < count; occurrence++)
            {
                var daysAgo = 3 * index + 7 * occurrence;
                interactions.Add(GenerateInteraction(ref interactionSequence, index, BaseDate.AddDays(-daysAgo), random));
            }
        }

        return (customers, interactions);
    }

    private static CustomerDto BuildCustomer(
        int index, string fullName, string city, string segment, string relationshipManagerId, string status) =>
        new(
            Id: FormatCustomerId(index),
            FullName: fullName,
            Email: $"customer{index:0000}@example.test",
            Phone: FormatPhone(index),
            AccountReference: FormatAccountReference(index),
            Segment: segment,
            City: city,
            PreferredLanguage: Language,
            RelationshipManagerId: relationshipManagerId,
            Status: status,
            Synthetic: true,
            UpdatedAtUtc: BaseDate);

    private static InteractionDto GenerateInteraction(
        ref int sequence, int customerIndex, DateTime occurredAtUtc, Random random)
    {
        var type = InteractionTypes[random.Next(InteractionTypes.Length)];
        var outcome = InteractionOutcomes[random.Next(InteractionOutcomes.Length)];
        var summary = InteractionSummaries[random.Next(InteractionSummaries.Length)];
        var nextAction = outcome == "FollowUpRequired"
            ? $"Liên hệ lại khách hàng trước ngày {occurredAtUtc.AddDays(7):yyyy-MM-dd}"
            : null;

        var interaction = new InteractionDto(
            Id: FormatInteractionId(sequence),
            CustomerId: FormatCustomerId(customerIndex),
            Type: type,
            OccurredAtUtc: occurredAtUtc,
            Summary: summary,
            Outcome: outcome,
            NextAction: nextAction,
            Synthetic: true);

        sequence++;
        return interaction;
    }

    private static string FormatCustomerId(int index) => $"CUS-{index:0000}";

    private static string FormatInteractionId(int index) => $"INT-{index:0000}";

    private static string FormatPhone(int index) => $"09{index:00000000}";

    private static string FormatAccountReference(int index) => $"{index:000000000000}";
}
