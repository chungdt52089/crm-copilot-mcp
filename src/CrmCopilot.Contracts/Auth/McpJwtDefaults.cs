namespace CrmCopilot.Contracts.Auth;

/// <summary>
/// P0-13: the shared shape of the bearer token CrmCopilot.Web mints and CrmCopilot.McpServer
/// validates. Both processes read the same signing key from the environment (a shared symmetric
/// secret — illustrative, not a production key-management design), so every literal that must
/// match on both sides lives here rather than being duplicated as two string constants.
/// </summary>
public static class McpJwtDefaults
{
    /// <summary>Flat config key, matching .env.example exactly.</summary>
    public const string SigningKeyConfigKey = "MCP_JWT_SIGNING_KEY";

    public const string Issuer = "crm-copilot-web";
    public const string Audience = "crm-copilot-mcp";

    /// <summary>Claim carrying the authenticated userId.</summary>
    public const string UserIdClaim = "sub";

    /// <summary>Claim carrying the role. Both sides turn off inbound claim mapping so this stays
    /// the literal name on the wire rather than being rewritten to a ClaimTypes.* URI.</summary>
    public const string RoleClaim = "role";

    /// <summary>HMAC-SHA256 needs a key of at least 256 bits; a shorter one throws only when the
    /// first token is signed, so both hosts check this at startup instead.</summary>
    public const int MinimumSigningKeyBytes = 32;
}
