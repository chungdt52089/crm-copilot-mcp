using System.Text.Json;
using CrmCopilot.Contracts.Api;
using Microsoft.AspNetCore.Identity;

namespace CrmCopilot.Web.Auth;

/// <summary>
/// P0-12 (WP1) illustrative user store over synthetic data — deliberately NOT production auth:
/// no user management, no password change, no lockout, no SSO (docs/01 PD-020).
///
/// Loads data/auth/users.json once in the constructor and fails fast if it is missing or unusable,
/// same convention as CrmCopilot.MockCrmApi.Data.CrmDatasetLoader. The path is resolved via
/// AppContext.BaseDirectory rather than the process working directory so behaviour is identical
/// under `dotnet run` and under the WebApplicationFactory test host — the file is copied into the
/// build output by CrmCopilot.Web.csproj's CopyToOutputDirectory item, and flows transitively into
/// any project that references CrmCopilot.Web.
///
/// Registered as a singleton: the file is read exactly once per process.
/// </summary>
internal sealed class UserStore
{
    private readonly Dictionary<string, AuthUser> _users;
    private readonly PasswordHasher<AuthUser> _hasher = new();

    public UserStore()
        : this(Path.Combine(AppContext.BaseDirectory, "data", "auth", "users.json"))
    {
    }

    internal UserStore(string usersFilePath)
    {
        AuthUsersFile? file;
        try
        {
            using var stream = File.OpenRead(usersFilePath);
            file = JsonSerializer.Deserialize<AuthUsersFile>(stream, CrmJsonOptions.Default);
        }
        catch (Exception ex)
        {
            // The path is safe to name (it is a build-output location, not a secret); the caught
            // exception is carried as InnerException only, never interpolated into this message.
            throw new InvalidOperationException($"Failed to read the auth user file at '{usersFilePath}'.", ex);
        }

        if (file?.Users is not { Count: > 0 } users)
        {
            throw new InvalidOperationException($"The auth user file at '{usersFilePath}' contains no users.");
        }

        _users = users.ToDictionary(user => user.UserId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the matching user, or <c>null</c> when the user id is unknown OR the password is
    /// wrong — the caller must not be able to tell those two cases apart (no user enumeration).
    /// </summary>
    public AuthUser? Validate(string? userId, string? password)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(password) ||
            !_users.TryGetValue(userId, out var user))
        {
            return null;
        }

        return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed
            ? null
            : user;
    }
}
