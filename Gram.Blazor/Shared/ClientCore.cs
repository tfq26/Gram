using System.Net;
using System.Text.RegularExpressions;

namespace Gram.Blazor.Shared;

/// <summary>
/// Pluggable authentication provider for client requests, supporting dynamic token injection and refreshing.
/// </summary>
public interface IAuthenticationProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Simple in-memory authentication provider holding a static access token.
/// </summary>
public sealed class StaticTokenAuthenticationProvider : IAuthenticationProvider
{
    private string? _token;

    public StaticTokenAuthenticationProvider(string? token = null)
    {
        _token = token;
    }

    public void SetToken(string? token) => _token = token;
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(_token);
    public Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>
/// Normalized client-side representation of an API error containing structured status and error codes.
/// </summary>
public sealed class GramApiException : HttpRequestException
{
    public string ErrorCode { get; }
    public string? TraceId { get; }

    public GramApiException(HttpStatusCode statusCode, string errorCode, string message, string? traceId = null)
        : base($"Gram API Error [{errorCode}] (HTTP {(int)statusCode}): {message}", null, statusCode)
    {
        ErrorCode = errorCode;
        TraceId = traceId;
    }
}

/// <summary>
/// Structured client logger helper that automatically strips authorization headers,
/// bearer tokens, passwords, and API secrets from log entries and URLs.
/// </summary>
public static class RedactedClientLogger
{
    private static readonly Regex TokenRegex = new(
        @"(bearer\s+)[a-zA-Z0-9_\-\.]+|((?:token|secret|password|key|apikey)=)[^&\s]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return TokenRegex.Replace(input, "$1[REDACTED]$2[REDACTED]");
    }
}

/// <summary>
/// Represents the synchronization and version conflict state for a conversation.
/// </summary>
public sealed record ConversationSyncState(
    string ConversationId,
    int LocalVersion,
    int ServerVersion,
    bool HasConflict,
    int QueuedEditsCount,
    DateTimeOffset LastSyncedAt
);
