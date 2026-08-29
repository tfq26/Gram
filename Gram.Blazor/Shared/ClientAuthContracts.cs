namespace Gram.Blazor.Shared;

public sealed record DeviceSession(string DeviceId, string DeviceName, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsRevoked = false);
public sealed record AccessTokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt, DeviceSession Session);
public sealed record DeviceAuthorizationRequest(string DeviceName, string InstallationId);
public sealed record RefreshTokenRequest(string RefreshToken);

public interface IClientAuthApi
{
    Task<AccessTokenPair?> AuthorizeDeviceAsync(DeviceAuthorizationRequest request, CancellationToken cancellationToken = default);
    Task<AccessTokenPair?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeviceSession>> ListDevicesAsync(CancellationToken cancellationToken = default);
    Task RevokeDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
}
