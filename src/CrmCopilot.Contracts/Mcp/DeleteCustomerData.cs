namespace CrmCopilot.Contracts.Mcp;

/// <summary>
/// delete_customer success data (P0-14). Carries the id alone — never a CustomerDto — so the
/// confirmation of a destructive action cannot echo the deleted record's name, email, phone or
/// account reference back to the caller. The id itself is the same synthetic reference already
/// embedded in the tool's sourceId, not PII (docs/08 §6).
/// </summary>
public sealed record DeleteCustomerData(string CustomerId);
