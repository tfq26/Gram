using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;

namespace Gram.Blazor.Shared;

public interface IAuthService
{
    AuthSettings Settings { get; }
    GramUser CurrentUser { get; }
    Task<GramUser> GetCurrentUserAsync();
    Task<AuthResult> SignInWithEmailAsync(string email, string password);
    Task<AuthResult> SignUpWithEmailAsync(string name, string email, string password);
    Task<bool> ValidateSessionTokenAsync(string sessionToken);
    Task<bool> UpdateProfileAsync(string name, string nickname);
    Task<bool> UpdateAvatarAsync(string? imageBase64OrUrl);
    Task<AuthResult> ResetPasswordAsync(string currentPassword, string newPassword);
    Task<PasswordResetRequestResult> RequestPasswordResetAsync(string email, string? baseUrl = null);
    Task<PasswordResetCompleteResult> CompletePasswordResetAsync(string email, string codeOrToken, string newPassword);
    Task<List<PasskeyRecord>> GetPasskeysAsync();
    Task<AuthResult> RegisterPasskeyAsync(string credentialId, string publicKey, string name);
    Task<AuthResult> RegisterPasskeyAsync();
    Task<AuthResult> SignInWithPasskeyAsync(string credentialId);
    Task<bool> DeletePasskeyAsync(string credentialId);
    string GetSocialLoginUrl(string provider, string callbackUrl);
    Task LogoutAsync();
    Task SetAuthProviderAsync(AuthProviderType provider, string? betterAuthUrl = null);
    event Action? OnAuthStateChanged;
}

public sealed class GramAuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StorageService _storageService;
    private readonly IDatabaseProvider _db;
    private readonly IEmailService? _emailService;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly string _sessionTokenFilePath;
    private GramUser _currentUser;

    public AuthSettings Settings { get; } = new();
    public GramUser CurrentUser => _currentUser;
    public event Action? OnAuthStateChanged;

    public GramAuthService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        StorageService storageService,
        IDatabaseProvider db,
        IEmailService? emailService = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _storageService = storageService;
        _db = db;
        _emailService = emailService;
        _httpContextAccessor = httpContextAccessor;
        _sessionTokenFilePath = _storageService.GetFilePath("auth_session.json");

        // Load configuration from appsettings, environment variables, or local config
        var providerStr = _configuration["AUTH_PROVIDER"] ?? _configuration["Auth:Provider"] ?? "None";
        if (Enum.TryParse<AuthProviderType>(providerStr, true, out var parsedProvider))
        {
            Settings.Provider = parsedProvider;
        }

        Settings.BetterAuthUrl = _configuration["BETTER_AUTH_URL"] ?? _configuration["Auth:BetterAuthUrl"] ?? Settings.BetterAuthUrl;
        Settings.BetterAuthSecret = _configuration["BETTER_AUTH_SECRET"] ?? _configuration["Auth:BetterAuthSecret"];
        Settings.OidcAuthority = _configuration["OIDC_AUTHORITY"] ?? _configuration["Auth:OidcAuthority"];
        Settings.OidcClientId = _configuration["OIDC_CLIENT_ID"] ?? _configuration["Auth:OidcClientId"];
        Settings.OidcClientSecret = _configuration["OIDC_CLIENT_SECRET"] ?? _configuration["Auth:OidcClientSecret"];
        Settings.ProxyUserHeader = _configuration["PROXY_USER_HEADER"] ?? _configuration["Auth:ProxyUserHeader"] ?? Settings.ProxyUserHeader;
        Settings.ProxyEmailHeader = _configuration["PROXY_EMAIL_HEADER"] ?? _configuration["Auth:ProxyEmailHeader"] ?? Settings.ProxyEmailHeader;
        Settings.ProxyNameHeader = _configuration["PROXY_NAME_HEADER"] ?? _configuration["Auth:ProxyNameHeader"] ?? Settings.ProxyNameHeader;

        _currentUser = InitializeUser();
        _ = TryRestoreSessionAsync();
    }

    private GramUser InitializeUser()
    {
        // 1. Check for Reverse Proxy Headers (Authentik / Authelia / Traefik / Cloudflare Access)
        if (_httpContextAccessor?.HttpContext != null)
        {
            var headers = _httpContextAccessor.HttpContext.Request.Headers;
            var proxyUser = headers[Settings.ProxyUserHeader].FirstOrDefault();
            var proxyEmail = headers[Settings.ProxyEmailHeader].FirstOrDefault();
            var proxyName = headers[Settings.ProxyNameHeader].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(proxyUser) || !string.IsNullOrWhiteSpace(proxyEmail))
            {
                var email = proxyEmail ?? $"{proxyUser}@internal";
                var name = proxyName ?? proxyUser ?? email;
                return new GramUser
                {
                    Id = proxyUser ?? email,
                    Name = name,
                    Email = email,
                    IsAuthenticated = true,
                    Roles = ["user"]
                };
            }
        }

        // 2. Default: Unauthenticated Guest (Requires login)
        return new GramUser
        {
            Id = "",
            Name = "Guest",
            Email = "",
            IsAuthenticated = false,
            Roles = []
        };
    }

    private async Task TryRestoreSessionAsync()
    {
        try
        {
            if (File.Exists(_sessionTokenFilePath))
            {
                var json = await File.ReadAllTextAsync(_sessionTokenFilePath);
                var tokenData = JsonNode.Parse(json);
                var token = tokenData?["token"]?.ToString();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    await ValidateSessionTokenAsync(token);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthService] Error restoring session: {ex.Message}");
        }
    }

    private async Task SaveSessionTokenAsync(string? token, GramUser? user = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (File.Exists(_sessionTokenFilePath)) File.Delete(_sessionTokenFilePath);
            }
            else
            {
                var payload = new JsonObject
                {
                    ["token"] = token,
                    ["userId"] = user?.Id ?? "",
                    ["savedAt"] = DateTimeOffset.UtcNow.ToString("o")
                };
                await File.WriteAllTextAsync(_sessionTokenFilePath, payload.ToJsonString());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AuthService] Error persisting session token: {ex.Message}");
        }
    }

    public Task<GramUser> GetCurrentUserAsync()
    {
        return Task.FromResult(_currentUser);
    }

    public async Task<AuthResult> SignInWithEmailAsync(string email, string password)
    {
        if (Settings.Provider == AuthProviderType.BetterAuth)
        {
            return await SignInWithBetterAuthAsync(email, password);
        }

        // Native SQLite Database Auth
        try
        {
            var user = await _db.GetUserByEmailAsync(email);
            if (user == null)
            {
                return new AuthResult { Success = false, ErrorMessage = "No user found with this email address." };
            }

            var isValid = SqliteDatabaseService.VerifyPassword(password, user.PasswordHash, user.Salt);
            if (!isValid)
            {
                return new AuthResult { Success = false, ErrorMessage = "Incorrect password." };
            }

            var session = await _db.CreateSessionAsync(user.Id);
            _currentUser = new GramUser
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Image = user.Image,
                IsAuthenticated = true,
                Roles = [user.Role],
                SessionToken = session.Token,
                ExpiresAt = session.ExpiresAt
            };

            await SaveSessionTokenAsync(session.Token, _currentUser);
            OnAuthStateChanged?.Invoke();
            return new AuthResult { Success = true, User = _currentUser, Token = session.Token };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Database authentication error: {ex.Message}" };
        }
    }

    public async Task<AuthResult> SignUpWithEmailAsync(string name, string email, string password)
    {
        if (Settings.Provider == AuthProviderType.BetterAuth)
        {
            return await SignUpWithBetterAuthAsync(name, email, password);
        }

        // Native SQLite Database Registration
        try
        {
            var existing = await _db.GetUserByEmailAsync(email);
            if (existing != null)
            {
                return new AuthResult { Success = false, ErrorMessage = "A user with this email already exists. Please sign in." };
            }

            if (password.Length < 6)
            {
                return new AuthResult { Success = false, ErrorMessage = "Password must be at least 6 characters." };
            }

            var createdUser = await _db.CreateUserAsync(email, name, password, "user");
            var session = await _db.CreateSessionAsync(createdUser.Id);

            _currentUser = new GramUser
            {
                Id = createdUser.Id,
                Name = createdUser.Name,
                Email = createdUser.Email,
                IsAuthenticated = true,
                Roles = [createdUser.Role],
                SessionToken = session.Token,
                ExpiresAt = session.ExpiresAt
            };

            await SaveSessionTokenAsync(session.Token, _currentUser);
            OnAuthStateChanged?.Invoke();
            return new AuthResult { Success = true, User = _currentUser, Token = session.Token };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Registration failed: {ex.Message}" };
        }
    }

    private async Task<AuthResult> SignInWithBetterAuthAsync(string email, string password)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var endpoint = $"{Settings.BetterAuthUrl.TrimEnd('/')}/api/auth/sign-in/email";

            var payload = new JsonObject
            {
                ["email"] = email,
                ["password"] = password
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            var res = await client.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (res.IsSuccessStatusCode)
            {
                var sessionRes = JsonSerializer.Deserialize<BetterAuthSessionResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var user = sessionRes?.User;
                var token = sessionRes?.Session?.Token;

                if (user != null)
                {
                    _currentUser = new GramUser
                    {
                        Id = user.Id,
                        Name = user.Name ?? user.Email ?? email,
                        Email = user.Email ?? email,
                        Image = user.Image,
                        IsAuthenticated = true,
                        Roles = [user.Role ?? "user"],
                        SessionToken = token
                    };

                    await SaveSessionTokenAsync(token, _currentUser);
                    OnAuthStateChanged?.Invoke();
                    return new AuthResult { Success = true, User = _currentUser, Token = token };
                }
            }

            var errNode = JsonNode.Parse(json);
            var message = errNode?["message"]?.ToString() ?? errNode?["error"]?.ToString() ?? "Invalid email or password.";
            return new AuthResult { Success = false, ErrorMessage = message };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Could not connect to Better Auth server ({Settings.BetterAuthUrl}): {ex.Message}" };
        }
    }

    private async Task<AuthResult> SignUpWithBetterAuthAsync(string name, string email, string password)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var endpoint = $"{Settings.BetterAuthUrl.TrimEnd('/')}/api/auth/sign-up/email";

            var payload = new JsonObject
            {
                ["name"] = name,
                ["email"] = email,
                ["password"] = password
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            var res = await client.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();

            if (res.IsSuccessStatusCode)
            {
                var sessionRes = JsonSerializer.Deserialize<BetterAuthSessionResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var user = sessionRes?.User;
                var token = sessionRes?.Session?.Token;

                if (user != null)
                {
                    _currentUser = new GramUser
                    {
                        Id = user.Id,
                        Name = user.Name ?? name,
                        Email = user.Email ?? email,
                        Image = user.Image,
                        IsAuthenticated = true,
                        Roles = [user.Role ?? "user"],
                        SessionToken = token
                    };

                    await SaveSessionTokenAsync(token, _currentUser);
                    OnAuthStateChanged?.Invoke();
                    return new AuthResult { Success = true, User = _currentUser, Token = token };
                }
            }

            var errNode = JsonNode.Parse(json);
            var message = errNode?["message"]?.ToString() ?? errNode?["error"]?.ToString() ?? "Registration failed.";
            return new AuthResult { Success = false, ErrorMessage = message };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Could not connect to Better Auth server ({Settings.BetterAuthUrl}): {ex.Message}" };
        }
    }

    public async Task<bool> ValidateSessionTokenAsync(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)) return false;

        // 1. Try SQLite Database first
        try
        {
            var user = await _db.ValidateSessionAsync(sessionToken);
            if (user != null)
            {
                _currentUser = new GramUser
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    Image = user.Image,
                    IsAuthenticated = true,
                    Roles = [user.Role],
                    SessionToken = sessionToken
                };
                OnAuthStateChanged?.Invoke();
                return true;
            }
        }
        catch {}

        // 2. If Better Auth enabled, try Better Auth validation
        if (Settings.Provider == AuthProviderType.BetterAuth)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var endpoint = $"{Settings.BetterAuthUrl.TrimEnd('/')}/api/auth/get-session";

                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                req.Headers.Add("Authorization", $"Bearer {sessionToken}");

                var res = await client.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    var sessionRes = JsonSerializer.Deserialize<BetterAuthSessionResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var user = sessionRes?.User;
                    if (user != null)
                    {
                        _currentUser = new GramUser
                        {
                            Id = user.Id,
                            Name = user.Name ?? user.Email ?? "User",
                            Email = user.Email,
                            Image = user.Image,
                            IsAuthenticated = true,
                            Roles = [user.Role ?? "user"],
                            SessionToken = sessionToken
                        };
                        OnAuthStateChanged?.Invoke();
                        return true;
                    }
                }
            }
            catch {}
        }

        return false;
    }

    public async Task<bool> UpdateProfileAsync(string name, string nickname)
    {
        _currentUser.Name = string.IsNullOrWhiteSpace(name) ? _currentUser.Name : name.Trim();
        _currentUser.Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();

        try
        {
            if (!string.IsNullOrWhiteSpace(_currentUser.Id))
            {
                await _db.UpdateUserProfileAsync(_currentUser.Id, _currentUser.Name, _currentUser.Image);
            }
            await SaveSessionTokenAsync(_currentUser.SessionToken, _currentUser);
        }
        catch {}

        OnAuthStateChanged?.Invoke();
        return true;
    }

    public async Task<bool> UpdateAvatarAsync(string? imageBase64OrUrl)
    {
        _currentUser.Image = string.IsNullOrWhiteSpace(imageBase64OrUrl) ? null : imageBase64OrUrl.Trim();

        try
        {
            if (!string.IsNullOrWhiteSpace(_currentUser.Id))
            {
                await _db.UpdateUserProfileAsync(_currentUser.Id, _currentUser.Name, _currentUser.Image);
            }
            await SaveSessionTokenAsync(_currentUser.SessionToken, _currentUser);
        }
        catch {}

        OnAuthStateChanged?.Invoke();
        return true;
    }

    public async Task<AuthResult> ResetPasswordAsync(string currentPassword, string newPassword)
    {
        if (newPassword.Length < 6)
        {
            return new AuthResult { Success = false, ErrorMessage = "New password must be at least 6 characters." };
        }

        // 1. Better Auth Password Reset
        if (Settings.Provider == AuthProviderType.BetterAuth)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var endpoint = $"{Settings.BetterAuthUrl.TrimEnd('/')}/api/auth/change-password";

                var payload = new JsonObject
                {
                    ["currentPassword"] = currentPassword,
                    ["newPassword"] = newPassword
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                req.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(_currentUser.SessionToken))
                {
                    req.Headers.Add("Authorization", $"Bearer {_currentUser.SessionToken}");
                }

                var res = await client.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    return new AuthResult { Success = true };
                }

                var json = await res.Content.ReadAsStringAsync();
                var errNode = JsonNode.Parse(json);
                return new AuthResult { Success = false, ErrorMessage = errNode?["message"]?.ToString() ?? "Could not change password." };
            }
            catch (Exception ex)
            {
                return new AuthResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // 2. SQLite Database Password Reset
        try
        {
            var user = await _db.GetUserByIdAsync(_currentUser.Id);
            if (user == null && !string.IsNullOrWhiteSpace(_currentUser.Email))
            {
                user = await _db.GetUserByEmailAsync(_currentUser.Email);
            }

            if (user != null)
            {
                var valid = SqliteDatabaseService.VerifyPassword(currentPassword, user.PasswordHash, user.Salt);
                if (!valid)
                {
                    return new AuthResult { Success = false, ErrorMessage = "Current password does not match." };
                }

                await _db.UpdatePasswordAsync(user.Id, newPassword);
                return new AuthResult { Success = true };
            }

            return new AuthResult { Success = false, ErrorMessage = "User record not found in database." };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Database error: {ex.Message}" };
        }
    }

    public async Task<PasswordResetRequestResult> RequestPasswordResetAsync(string email, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new PasswordResetRequestResult { Success = false, ErrorMessage = "Email address is required." };
        }

        try
        {
            var user = await _db.GetUserByEmailAsync(email.Trim());
            if (user == null)
            {
                return new PasswordResetRequestResult { Success = false, ErrorMessage = "No account found with this email address." };
            }

            // Generate cryptographically secure 6-digit verification code
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            var expiresAt = DateTime.UtcNow.AddMinutes(15);

            var saved = await _db.SavePasswordResetTokenAsync(user.Id, code, token, expiresAt);
            if (!saved)
            {
                return new PasswordResetRequestResult { Success = false, ErrorMessage = "Could not initialize reset session." };
            }

            var baseUri = baseUrl?.TrimEnd('/') ?? "http://localhost:7337";
            var resetUrl = $"{baseUri}/login?resetCode={code}&email={Uri.EscapeDataString(user.Email)}";

            if (_emailService != null)
            {
                await _emailService.SendPasswordResetEmailAsync(user.Email, user.Name ?? user.Email, code, resetUrl);
            }

            var isDev = _emailService == null || !_emailService.IsConfigured;
            return new PasswordResetRequestResult
            {
                Success = true,
                IsDevMode = isDev,
                DevCode = isDev ? code : null,
                DevResetUrl = isDev ? resetUrl : null
            };
        }
        catch (Exception ex)
        {
            return new PasswordResetRequestResult { Success = false, ErrorMessage = $"Error processing password reset: {ex.Message}" };
        }
    }

    public async Task<PasswordResetCompleteResult> CompletePasswordResetAsync(string email, string codeOrToken, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(codeOrToken))
        {
            return new PasswordResetCompleteResult { Success = false, ErrorMessage = "Email and verification code are required." };
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return new PasswordResetCompleteResult { Success = false, ErrorMessage = "New password must be at least 6 characters." };
        }

        try
        {
            var trimmedEmail = email.Trim();
            var trimmedCode = codeOrToken.Trim();

            var resetRecord = await _db.GetPasswordResetByCodeAsync(trimmedEmail, trimmedCode)
                           ?? await _db.GetPasswordResetByTokenAsync(trimmedCode);

            if (resetRecord == null)
            {
                return new PasswordResetCompleteResult { Success = false, ErrorMessage = "Invalid verification code or email address." };
            }

            if (resetRecord.Used)
            {
                return new PasswordResetCompleteResult { Success = false, ErrorMessage = "This reset code has already been used. Please request a new one." };
            }

            if (DateTime.UtcNow > resetRecord.ExpiresAt)
            {
                return new PasswordResetCompleteResult { Success = false, ErrorMessage = "This verification code has expired. Please request a new one." };
            }

            // Update user password
            var updated = await _db.UpdatePasswordAsync(resetRecord.UserId, newPassword);
            if (!updated)
            {
                return new PasswordResetCompleteResult { Success = false, ErrorMessage = "Failed to update password in database." };
            }

            // Invalidate the reset token
            await _db.InvalidatePasswordResetTokenAsync(resetRecord.Id);

            // Automatically create authenticated session
            var user = await _db.GetUserByIdAsync(resetRecord.UserId);
            if (user != null)
            {
                var session = await _db.CreateSessionAsync(user.Id);
                _currentUser = new GramUser
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    Image = user.Image,
                    IsAuthenticated = true,
                    Roles = [user.Role],
                    SessionToken = session.Token,
                    ExpiresAt = session.ExpiresAt
                };

                await SaveSessionTokenAsync(session.Token, _currentUser);
                OnAuthStateChanged?.Invoke();
                return new PasswordResetCompleteResult { Success = true, User = _currentUser, Token = session.Token };
            }

            return new PasswordResetCompleteResult { Success = true };
        }
        catch (Exception ex)
        {
            return new PasswordResetCompleteResult { Success = false, ErrorMessage = $"Failed to complete password reset: {ex.Message}" };
        }
    }

    public async Task<List<PasskeyRecord>> GetPasskeysAsync()
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.Id))
        {
            return [];
        }

        return await _db.GetPasskeysForUserAsync(_currentUser.Id);
    }

    public async Task<AuthResult> RegisterPasskeyAsync(string credentialId, string publicKey, string name)
    {
        string userId = _currentUser.Id;

        if (string.IsNullOrWhiteSpace(userId) || !_currentUser.IsAuthenticated)
        {
            try
            {
                var defaultUser = await _db.GetUserByEmailAsync("user@gram.local");
                if (defaultUser == null)
                {
                    defaultUser = await _db.CreateUserAsync("user@gram.local", "Gram User", Guid.NewGuid().ToString("N")[..12], "user");
                }

                userId = defaultUser.Id;
                var session = await _db.CreateSessionAsync(userId);
                _currentUser = new GramUser
                {
                    Id = defaultUser.Id,
                    Name = defaultUser.Name,
                    Email = defaultUser.Email,
                    Image = defaultUser.Image,
                    IsAuthenticated = true,
                    HasPasskey = true,
                    Roles = [defaultUser.Role],
                    SessionToken = session.Token,
                    ExpiresAt = session.ExpiresAt
                };
                await SaveSessionTokenAsync(session.Token, _currentUser);
            }
            catch (Exception ex)
            {
                return new AuthResult { Success = false, ErrorMessage = $"Could not initialize user account: {ex.Message}" };
            }
        }

        try
        {
            var saved = await _db.SavePasskeyAsync(userId, credentialId, publicKey, name);
            if (saved)
            {
                _currentUser.HasPasskey = true;
                OnAuthStateChanged?.Invoke();
                return new AuthResult { Success = true, User = _currentUser };
            }

            return new AuthResult { Success = false, ErrorMessage = "Failed to save passkey in database." };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Passkey error: {ex.Message}" };
        }
    }

    public async Task<AuthResult> RegisterPasskeyAsync()
    {
        _currentUser.HasPasskey = true;
        OnAuthStateChanged?.Invoke();
        await Task.CompletedTask;
        return new AuthResult { Success = true, User = _currentUser };
    }

    public async Task<AuthResult> SignInWithPasskeyAsync(string credentialId)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            return new AuthResult { Success = false, ErrorMessage = "Invalid passkey credential." };
        }

        try
        {
            var passkey = await _db.GetPasskeyByIdAsync(credentialId);
            if (passkey == null)
            {
                return new AuthResult { Success = false, ErrorMessage = "Passkey not recognized. Please sign in with email/password to register your passkey." };
            }

            var user = await _db.GetUserByIdAsync(passkey.UserId);
            if (user == null)
            {
                return new AuthResult { Success = false, ErrorMessage = "User account not found for this passkey." };
            }

            var session = await _db.CreateSessionAsync(user.Id);

            _currentUser = new GramUser
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Image = user.Image,
                IsAuthenticated = true,
                HasPasskey = true,
                Roles = [user.Role],
                SessionToken = session.Token,
                ExpiresAt = session.ExpiresAt
            };

            await SaveSessionTokenAsync(session.Token, _currentUser);
            OnAuthStateChanged?.Invoke();
            return new AuthResult { Success = true, User = _currentUser, Token = session.Token };
        }
        catch (Exception ex)
        {
            return new AuthResult { Success = false, ErrorMessage = $"Passkey sign-in failed: {ex.Message}" };
        }
    }

    public async Task<bool> DeletePasskeyAsync(string credentialId)
    {
        if (!_currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(_currentUser.Id))
        {
            return false;
        }

        try
        {
            var deleted = await _db.DeletePasskeyAsync(_currentUser.Id, credentialId);
            if (deleted)
            {
                var remaining = await _db.HasPasskeysAsync(_currentUser.Id);
                _currentUser.HasPasskey = remaining;
                OnAuthStateChanged?.Invoke();
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public string GetSocialLoginUrl(string provider, string callbackUrl)
    {
        var baseUrl = Settings.BetterAuthUrl.TrimEnd('/');
        var encodedCallback = Uri.EscapeDataString(callbackUrl);
        return $"{baseUrl}/api/auth/sign-in/social?provider={provider.ToLowerInvariant()}&callbackURL={encodedCallback}";
    }

    public async Task LogoutAsync()
    {
        var token = _currentUser.SessionToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            try { await _db.InvalidateSessionAsync(token); } catch {}
        }

        await SaveSessionTokenAsync(null);

        _currentUser = new GramUser
        {
            Id = "guest",
            Name = "Guest",
            Email = null,
            IsAuthenticated = false,
            Roles = []
        };

        OnAuthStateChanged?.Invoke();
    }

    public async Task SetAuthProviderAsync(AuthProviderType provider, string? betterAuthUrl = null)
    {
        Settings.Provider = provider;
        if (!string.IsNullOrWhiteSpace(betterAuthUrl))
        {
            Settings.BetterAuthUrl = betterAuthUrl;
        }

        if (provider == AuthProviderType.None)
        {
            _currentUser = InitializeUser();
        }

        OnAuthStateChanged?.Invoke();
        await Task.CompletedTask;
    }
}

public sealed class GramAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly IAuthService _authService;

    public GramAuthStateProvider(IAuthService authService)
    {
        _authService = authService;
        _authService.OnAuthStateChanged += NotifyStateChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var principal = _authService.CurrentUser.ToClaimsPrincipal();
        return Task.FromResult(new AuthenticationState(principal));
    }

    private void NotifyStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void Dispose()
    {
        _authService.OnAuthStateChanged -= NotifyStateChanged;
    }
}
