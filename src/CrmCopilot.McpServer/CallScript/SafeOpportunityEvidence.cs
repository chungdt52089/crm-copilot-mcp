namespace CrmCopilot.McpServer.CallScript;

/// <summary>
/// The ONLY opportunity shape that may enter a Gemini prompt (plan D12 / Amendment A2/A5).
///
/// Two deliberate omissions, both load-bearing rather than incidental:
/// 1. No CustomerId. The opportunity id in <see cref="SourceId"/> is what the model needs in order
///    to cite its evidence; the customer id adds nothing to the generation and is simply not sent.
/// 2. No exact AmountVnd. <see cref="AmountBand"/> carries a coarse bucket instead, so a customer
///    financial figure never reaches the model while the script can still be calibrated to the
///    rough size of the deal. The exact figure still reaches the RM and the UI over the trusted
///    local response path, exactly as CustomerDto already does.
///
/// A raw <see cref="CrmCopilot.Contracts.Crm.OpportunityDto"/> is never handed to the generator.
/// </summary>
internal sealed record SafeOpportunityEvidence(
    string SourceId,
    string ProductCode,
    string Stage,
    string Status,
    DateTime ExpectedCloseDateUtc,
    string AmountBand);
