using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrmCopilot.Contracts.Crm;
using CrmCopilot.Contracts.Knowledge;
using CrmCopilot.McpServer.Crm;
using CrmCopilot.McpServer.Email;
using CrmCopilot.McpServer.Knowledge;
using CrmCopilot.McpServer.Knowledge.Ingestion;
using CrmCopilot.Tests.Acceptance.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrmCopilot.Tests.Acceptance;

/// <summary>
/// The live (L) acceptance layer for T07/T08 — real Gemini, real Chroma, real Mock CRM API.
///
/// Opt-in, mirroring LiveRagAcceptanceTests/LiveEmailGenerationAcceptanceTests: reports **Skipped**,
/// never Passed, when credentials or the Mock CRM API are absent. A skip is not a pass: per the P0-09
/// verdict rule, a live gate that did not run caps the checkpoint at PARTIAL, and the deterministic
/// layer may never be substituted for it.
///
/// Uses the isolated "crm-copilot-knowledge-livetest" collection so the default dev collection is
/// never touched.
/// </summary>
public class LiveAcceptanceScenarioTests
{
    private const string LiveTestCollectionName = "crm-copilot-knowledge-livetest";

    /// <summary>
    /// The exact neutral fallback EmailTools prepends to the body when the model dropped or altered
    /// {{CUSTOMER_NAME}} (EmailTools.cs:557, mandated by docs/08_RAG_EMAIL_AND_PII_SPEC.md §6).
    /// Kept in sync with that literal — a drift here would silently weaken the greeting assertion.
    /// </summary>
    private const string NeutralGreeting = "Kính gửi Anh/Chị,";

    private static readonly Regex PlaceholderPattern = new(@"\{\{[A-Z_]+\}\}", RegexOptions.Compiled);

    private static readonly Regex VietnameseDiacriticPattern = new(
        "[àáảãạăằắẳẵặâầấẩẫậèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵđ]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FabricatedRatePattern = new(
        @"%|lãi\s*suất[^.]{0,20}\d", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool LiveAcceptanceCredentialsPresent =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GeminiEmbeddingOptions.ApiKeyConfigKey)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ChromaOptions.BaseUrlConfigKey)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MockCrmGatewayOptions.ConfigKey));

    [Fact(
        SkipUnless = nameof(LiveAcceptanceCredentialsPresent),
        Skip = "Opt-in live acceptance gate (P0-09 plan §9 condition C5): requires a real GEMINI_API_KEY, a " +
               "reachable CHROMA_BASE_URL, and a running CrmCopilot.MockCrmApi at MOCKCRM_API_BASE_URL. " +
               "Not run by default/CI. A skip caps the checkpoint verdict at PARTIAL — it is never a PASS.")]
    public async Task T07AndT08_AgainstRealGeminiChromaAndMockCrmApi()
    {
        var results = new List<ScenarioResult>();

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddCrmGateway(configuration);
        services.AddKnowledgeRetrieval(configuration);
        services.AddEmailGeneration();
        services.PostConfigure<ChromaOptions>(options => options.CollectionName = LiveTestCollectionName);
        using var provider = services.BuildServiceProvider();

        var documents = KnowledgeSourceLoader.LoadFromAppBaseDirectory();
        var ingestionService = provider.GetRequiredService<KnowledgeIngestionService>();
        await ingestionService.IngestAsync(documents, TestContext.Current.CancellationToken);

        var capturingGenerator = new CapturingEmailDraftGenerator(provider.GetRequiredService<IEmailDraftGenerator>());
        var tools = new EmailTools(
            provider.GetRequiredService<ICrmGateway>(),
            provider.GetRequiredService<IKnowledgeRetriever>(),
            capturingGenerator,
            new HttpContextAccessor(),
            NullLogger<EmailTools>.Instance);

        var stopwatch = Stopwatch.StartNew();
        var raw = await tools.GenerateEmail(
            ScenarioDatasetSeed.CanonicalCustomerId,
            "Follow-up nhu cầu gửi tiết kiệm kỳ hạn 6 tháng",
            "professional_warm",
            null,
            TestContext.Current.CancellationToken);
        stopwatch.Stop();

        var root = JsonDocument.Parse(raw).RootElement;
        results.Add(EvaluateT07(root, stopwatch.ElapsedMilliseconds));
        results.Add(EvaluateT08(capturingGenerator, stopwatch.ElapsedMilliseconds));

        var reportPath = ScenarioReportWriter.Write(results, "acceptance-scenarios-live.md");
        TestContext.Current.SendDiagnosticMessage($"Live acceptance scenario report: {reportPath}");

        var errored = results.Where(result => result.Outcome == ScenarioOutcome.Error).ToList();
        Assert.True(
            errored.Count == 0,
            "Live scenario(s) could not be evaluated: "
            + string.Join(" || ", errored.Select(result => $"{result.Id} — {result.FailureSummary}")));

        // Unlike the deterministic layer there is no ≥7/8 budget here: this gate covers only the two
        // scenarios that genuinely need the live path, so both must pass for condition C5 to hold.
        var failed = results.Where(result => result.Outcome != ScenarioOutcome.Pass).ToList();
        Assert.True(
            failed.Count == 0,
            "Live acceptance gate failed: "
            + string.Join(" || ", failed.Select(result => $"{result.Id} — {result.FailureSummary}")));
    }

    private static ScenarioResult EvaluateT07(JsonElement root, long durationMs)
    {
        var checklist = new ScenarioChecklist();

        checklist.RequireEqual("status = success", "success", root.GetProperty("status").GetString());

        var draft = root.GetProperty("data").GetProperty("draft");
        var subject = draft.GetProperty("subject").GetString() ?? string.Empty;
        var body = draft.GetProperty("body").GetString() ?? string.Empty;
        var sourceIds = draft.GetProperty("sourceIds").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty).ToList();

        checklist.Require("subject không rỗng", !string.IsNullOrWhiteSpace(subject), $"length={subject.Length}");
        checklist.Require("body không rỗng", !string.IsNullOrWhiteSpace(body), $"length={body.Length}");
        checklist.Require(
            "requiresHumanApproval = true",
            draft.GetProperty("requiresHumanApproval").GetBoolean(),
            "server-forced, không lấy từ output model");

        checklist.RequireContains(
            "grounded vào product canonical", sourceIds, ScenarioDatasetSeed.CanonicalProductSourceId);
        checklist.RequireEqual(
            "suggestedProductCode = PRD-SAV-006M",
            ScenarioDatasetSeed.CanonicalProductCode,
            draft.GetProperty("suggestedProductCode").GetString());
        checklist.Require(
            "cite ít nhất một email template",
            sourceIds.Any(id => id.StartsWith("kb:email-template:", StringComparison.Ordinal)),
            $"sourceIds=[{string.Join(", ", sourceIds)}]");

        // The greeting has exactly two legitimate outcomes under the P0-07 contract
        // (docs/08_RAG_EMAIL_AND_PII_SPEC.md §6, implemented at EmailTools.cs:552-557):
        //   - the model kept {{CUSTOMER_NAME}} ⇒ it is restored locally to the real synthetic name;
        //   - the model dropped/altered it     ⇒ EmailTools prepends the neutral "Kính gửi Anh/Chị,".
        // Both are asserted here as a disjunction. The live gate is deliberately NOT tightened into
        // "the name must always appear" — that would turn a documented, correct fallback into a
        // failure. The strict "placeholder in ⇒ name out" claim belongs to the deterministic layer,
        // where the model's output is controlled.
        var restoredName = body.Contains(ScenarioDatasetSeed.CanonicalCustomer.FullName, StringComparison.Ordinal);
        var neutralGreeting = body.StartsWith(NeutralGreeting, StringComparison.Ordinal);

        checklist.Require(
            "không còn placeholder thô trong subject/body",
            !PlaceholderPattern.IsMatch(subject) && !PlaceholderPattern.IsMatch(body),
            "cả hai nhánh greeting đều phải sạch placeholder");
        checklist.Require(
            "greeting đúng contract P0-07: tên đã restore HOẶC lời chào trung tính",
            restoredName || neutralGreeting,
            restoredName
                ? "nhánh quan sát được: placeholder được giữ, tên đã restore ở local"
                : neutralGreeting
                    ? "nhánh quan sát được: model làm mất placeholder, dùng lời chào trung tính (doc08 §6)"
                    : "KHÔNG khớp nhánh nào — body vừa thiếu tên vừa thiếu lời chào trung tính");

        checklist.Require(
            "subject và body là tiếng Việt có dấu",
            VietnameseDiacriticPattern.IsMatch(subject) && VietnameseDiacriticPattern.IsMatch(body),
            "output bị ASCII-hoá/mất dấu");
        checklist.Require(
            "không bịa lãi suất / số liệu ngoài evidence",
            !FabricatedRatePattern.IsMatch(subject) && !FabricatedRatePattern.IsMatch(body),
            "corpus knowledge không chứa ký tự '%' nào, nên mọi con số lãi suất đều là bịa");

        return ScenarioResult.From(
            ScenarioId.T07, "Email draft RAG (live)", "EmailTools + Gemini/Chroma thật",
            EvidenceClass.Live, checklist, durationMs);
    }

    private static ScenarioResult EvaluateT08(CapturingEmailDraftGenerator generator, long durationMs)
    {
        var checklist = new ScenarioChecklist();

        checklist.Require(
            "đã bắt được ít nhất một prompt context gửi Gemini",
            generator.CapturedContexts.Count > 0,
            $"count={generator.CapturedContexts.Count}");

        // The context is everything GeminiEmailDraftGenerator builds its prompt from, so a clean scan
        // here is a sound proof that no raw PII left the machine.
        var everythingSentToGemini = JsonSerializer.Serialize(generator.CapturedContexts);
        var leakedCount = ScenarioDatasetSeed.CanonicalPiiValues
            .Count(pii => everythingSentToGemini.Contains(pii, StringComparison.Ordinal));

        checklist.Require(
            "không giá trị PII thô nào rời máy tới Gemini",
            leakedCount == 0,
            $"số giá trị PII bị rò={leakedCount}/{ScenarioDatasetSeed.CanonicalPiiValues.Count}");

        checklist.Require(
            "prompt dùng placeholder thay cho tên thật",
            !everythingSentToGemini.Contains(ScenarioDatasetSeed.CanonicalCustomer.FullName, StringComparison.Ordinal),
            "tên khách hàng chỉ được restore ở local sau khi model trả lời");

        return ScenarioResult.From(
            ScenarioId.T08, "Safety / resilience (live)", "EmailTools + Gemini thật",
            EvidenceClass.Live, checklist, durationMs);
    }
}
