namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// Fixed constants for the generate_call_script Gemini generation call. Plain constants, not an
/// IOptions&lt;T&gt; — same convention as EmailGenerationOptions: there is no secret here to bind
/// from configuration.
/// </summary>
internal static class CallScriptGenerationOptions
{
    public const string ModelId = "gemini-3.5-flash-lite";

    /// <summary>Low temperature for grounded, low-variance generation.</summary>
    public const double Temperature = 0.2;

    /// <summary>1 initial attempt + 1 retry. Never a third attempt.</summary>
    public const int MaxAttempts = 2;

    public const string DefaultLanguage = "vi";

    /// <summary>Retrieval breadth, mirroring generate_email: a couple of playbooks and a few
    /// products is enough grounding without flooding the prompt.</summary>
    public const int CallScriptTopK = 2;

    public const int ProductTopK = 3;

    public const int InteractionLimit = 5;

    public const int OpportunityLimit = 5;

    public const int MaxObjectiveLength = 500;

    public const int MaxOpportunityIdLength = 40;

    /// <summary>The template pinned for the periodic-care fallback path (plan Amendment A6).</summary>
    public const string PeriodicCareScriptId = "CS-CALL-PERIODIC-CARE-01";

    public const string CustomerFollowUpObjective = "Chăm sóc định kỳ và cập nhật nhu cầu của khách hàng";
}
