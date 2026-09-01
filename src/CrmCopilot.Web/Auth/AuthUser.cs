namespace CrmCopilot.Web.Auth;

/// <summary>
/// P0-12 (WP1) on-disk shape of one synthetic demo user in data/auth/users.json. Internal on
/// purpose: <see cref="PasswordHash"/> must never leave CrmCopilot.Web. What goes out on the wire
/// is <see cref="AuthUserView"/>, which has no credential field at all.
/// </summary>
internal sealed record AuthUser(string UserId, string DisplayName, string Role, string PasswordHash);

/// <summary>Root of data/auth/users.json. <c>synthetic</c> mirrors the CRM/knowledge datasets'
/// own marker — these are illustrative credentials over synthetic data, not real accounts.</summary>
internal sealed record AuthUsersFile(bool Synthetic, List<AuthUser> Users);

/// <summary>The credential-free projection returned by POST /api/auth/login.</summary>
internal sealed record AuthUserView(string UserId, string DisplayName, string Role);
