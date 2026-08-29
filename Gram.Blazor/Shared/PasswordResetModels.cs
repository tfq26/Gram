using System;

namespace Gram.Blazor.Shared;

public sealed class PasswordResetRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(15);
    public bool Used { get; set; } = false;
}

public sealed class PasswordResetRequestResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DevCode { get; set; }
    public string? DevResetUrl { get; set; }
    public bool IsDevMode { get; set; }
}

public sealed class PasswordResetCompleteResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public GramUser? User { get; set; }
    public string? Token { get; set; }
}
