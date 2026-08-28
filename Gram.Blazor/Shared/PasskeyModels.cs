using System.Text.Json.Serialization;

namespace Gram.Blazor.Shared;

public sealed class PasskeyRecord
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public long Counter { get; set; } = 0;
    public string Name { get; set; } = "Passkey";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PasskeyRegisterJsResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("credentialId")]
    public string? CredentialId { get; set; }

    [JsonPropertyName("publicKey")]
    public string? PublicKey { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class PasskeyAuthJsResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("credentialId")]
    public string? CredentialId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
