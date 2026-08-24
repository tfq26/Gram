using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public enum AuthProviderType
{
    None,
    BetterAuth,
    Oidc,
    ProxyHeader
}

public sealed class AuthSettings
{
    public AuthProviderType Provider { get; set; } = AuthProviderType.None;
    
    // Better Auth settings
    public string BetterAuthUrl { get; set; } = "http://localhost:3000";
    public string? BetterAuthSecret { get; set; }
    
    // Generic OIDC settings (Authentik, Keycloak, etc.)
    public string? OidcAuthority { get; set; }
    public string? OidcClientId { get; set; }
    public string? OidcClientSecret { get; set; }
    public string OidcScope { get; set; } = "openid profile email";
    
    // Reverse Proxy Header Auth settings (Authentik / Authelia / Traefik / Nginx)
    public string ProxyUserHeader { get; set; } = "X-authentik-username";
    public string ProxyEmailHeader { get; set; } = "X-authentik-email";
    public string ProxyNameHeader { get; set; } = "X-authentik-name";
}

public sealed class GramUser
{
    public string Id { get; set; } = "local-admin";
    public string Name { get; set; } = "Local Admin";
    public string? Nickname { get; set; } = "Admin";
    public string? Email { get; set; } = "admin@local.gram";
    public string? Image { get; set; }
    public bool IsAuthenticated { get; set; } = true;
    public bool HasPasskey { get; set; } = false;
    public List<string> Roles { get; set; } = ["admin"];
    public string? SessionToken { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public ClaimsPrincipal ToClaimsPrincipal()
    {
        if (!IsAuthenticated)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Id),
            new(ClaimTypes.Name, Name)
        };

        if (!string.IsNullOrEmpty(Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, Email));
        }

        foreach (var role in Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "GramAuth");
        return new ClaimsPrincipal(identity);
    }
}

public sealed class AuthResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public GramUser? User { get; set; }
    public string? Token { get; set; }
}

public sealed class BetterAuthSessionResponse
{
    [JsonPropertyName("session")]
    public BetterAuthSession? Session { get; set; }

    [JsonPropertyName("user")]
    public BetterAuthUser? User { get; set; }
}

public sealed class BetterAuthSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class BetterAuthUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("email")]
    public string? Email { get; set; }
    
    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}
