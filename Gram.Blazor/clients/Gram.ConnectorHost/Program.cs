using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gram.ConnectorHost;
using Gram.Blazor.Shared;

var secret = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_HOST_SECRET");
if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("GRAM_CONNECTOR_HOST_SECRET is required.");
var dataRoot = Environment.GetEnvironmentVariable("GRAM_DATA_DIR");
if (string.IsNullOrWhiteSpace(dataRoot)) dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gram");
var installedRoot = Path.GetFullPath(Path.Combine(dataRoot, "connectors", "installed"));
Directory.CreateDirectory(installedRoot);
var credentialVault = new ConnectorCredentialVault(new StorageService());

var builder = WebApplication.CreateBuilder(args);
var socketPath = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_HOST_SOCKET");
if (!string.IsNullOrWhiteSpace(socketPath) && !OperatingSystem.IsWindows())
{
    socketPath = Path.GetFullPath(socketPath);
    var socketParent = Path.GetDirectoryName(socketPath) ?? throw new InvalidOperationException("Connector socket path is invalid.");
    Directory.CreateDirectory(socketParent);
    if (File.Exists(socketPath)) File.Delete(socketPath);
    builder.WebHost.ConfigureKestrel(options => options.ListenUnixSocket(socketPath));
}
else
{
    var hostUrl = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_HOST_URL") ?? "http://127.0.0.1:7340";
    if (!Uri.TryCreate(hostUrl, UriKind.Absolute, out var hostUri) || !(hostUri.Host is "127.0.0.1" or "localhost" or "::1"))
        throw new InvalidOperationException("Connector host TCP transport must bind to loopback.");
    builder.WebHost.UseUrls(hostUrl);
}
var app = builder.Build();
if (!string.IsNullOrWhiteSpace(socketPath) && !OperatingSystem.IsWindows())
{
    var finalSocketPath = socketPath;
    app.Lifetime.ApplicationStarted.Register(() => SetOwnerOnly(finalSocketPath));
}

app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/v1/egress", StringComparison.OrdinalIgnoreCase)) { await next(); return; }
    var supplied = context.Request.Headers["X-Gram-Connector-Secret"].FirstOrDefault() ?? "";
    var expectedBytes = Encoding.UTF8.GetBytes(secret);
    var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
    if (expectedBytes.Length != suppliedBytes.Length || !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", isolation = "separate-process", timestamp = DateTimeOffset.UtcNow }));
app.MapPost("/v1/execute", async (ConnectorHostExecuteRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.PackageId) || request.PackageId.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '.' or '_')))
        return Results.BadRequest(new ConnectorActionResult(false, "invalid_package_id"));
    var packageRoot = Path.GetFullPath(Path.Combine(installedRoot, request.PackageId));
    if (!packageRoot.StartsWith(installedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        return Results.BadRequest(new ConnectorActionResult(false, "invalid_package_path"));
    var assemblyPath = Path.Combine(packageRoot, "Connector.Validation.dll");
    if (!File.Exists(assemblyPath)) return Results.NotFound(new ConnectorActionResult(false, "package_not_found"));
    var manifestPath = Path.Combine(packageRoot, "connector.json");
    if (!File.Exists(manifestPath)) return Results.NotFound(new ConnectorActionResult(false, "manifest_not_found"));
    var manifest = JsonSerializer.Deserialize<ConnectorManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), BackendJson.Options);
    if (manifest?.ExecutionPolicy is null) return Results.BadRequest(new ConnectorActionResult(false, "execution_policy_missing"));
    if (!manifest.Id.Equals(request.Action.ConnectorId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new ConnectorActionResult(false, "connector_identity_mismatch"));

    var workerPath = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_WORKER_PATH") ?? Path.Combine(AppContext.BaseDirectory, "Gram.ConnectorWorker.dll");
    if (!File.Exists(workerPath)) return Results.Json(new ConnectorActionResult(false, "worker_not_found"), statusCode: StatusCodes.Status503ServiceUnavailable);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(manifest.ExecutionPolicy.TimeoutSeconds, 1, 300)));
    using var process = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = packageRoot } };
    process.StartInfo.ArgumentList.Add(workerPath);
    process.StartInfo.ArgumentList.Add(packageRoot);
    process.StartInfo.Environment.Clear();
    process.StartInfo.Environment["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
    process.StartInfo.Environment["DOTNET_ROOT"] = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "";
    process.StartInfo.Environment["GRAM_CONNECTOR_NETWORK_POLICY"] = manifest.ExecutionPolicy.AllowedDomains.Count == 0 ? "deny" : "proxy-required";
    process.StartInfo.Environment["GRAM_CONNECTOR_EGRESS_URL"] = Environment.GetEnvironmentVariable("GRAM_CONNECTOR_HOST_URL") ?? "http://127.0.0.1:7340";
    if (!string.IsNullOrWhiteSpace(socketPath)) process.StartInfo.Environment["GRAM_CONNECTOR_HOST_SOCKET"] = socketPath;
    process.StartInfo.Environment["GRAM_CONNECTOR_EGRESS_TOKEN"] = CreateExecutionToken(secret, request.PackageId, request.Action.UserId ?? "default", request.Action.ConnectionId ?? "default", DateTimeOffset.UtcNow.AddMinutes(2));
    try
    {
        process.Start();
        using var isolation = WindowsProcessIsolation.TryAssign(process, manifest.ExecutionPolicy.MemoryLimitMb * 1024L * 1024L);
        if (isolation is null)
        {
            try { process.Kill(true); } catch { }
            return Results.Json(new ConnectorActionResult(false, "isolation_unavailable", "Windows worker isolation could not be established."), statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request, BackendJson.Options));
        process.StandardInput.Close();
        var outputTask = ReadBoundedAsync(process.StandardOutput, Math.Clamp(manifest.ExecutionPolicy.MaxOutputBytes, 1024, 10_000_000), timeout.Token);
        var errorTask = ReadBoundedAsync(process.StandardError, 4000, timeout.Token);
        while (!process.HasExited)
        {
            timeout.Token.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.WorkingSet64 > manifest.ExecutionPolicy.MemoryLimitMb * 1024L * 1024L || process.TotalProcessorTime > TimeSpan.FromSeconds(manifest.ExecutionPolicy.TimeoutSeconds))
            {
                process.Kill(true);
                return Results.Json(new ConnectorActionResult(false, "resource_limit"), statusCode: StatusCodes.Status429TooManyRequests);
            }
            await Task.Delay(100, timeout.Token);
        }
        var output = await outputTask;
        var error = await errorTask;
        if (output.Truncated) return Results.Json(new ConnectorActionResult(false, "output_limit"), statusCode: StatusCodes.Status413PayloadTooLarge);
        if (process.ExitCode != 0) return Results.Json(new ConnectorActionResult(false, "worker_failed", error.Text.Length > 500 ? error.Text[..500] : error.Text), statusCode: StatusCodes.Status502BadGateway);
        return Results.Ok(JsonSerializer.Deserialize<ConnectorActionResult>(output.Text, BackendJson.Options) ?? new ConnectorActionResult(false, "worker_empty"));
    }
    catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(true); } catch { } return Results.Json(new ConnectorActionResult(false, "timeout"), statusCode: StatusCodes.Status408RequestTimeout); }
    catch (Exception ex) { try { if (!process.HasExited) process.Kill(true); } catch { } return Results.Json(new ConnectorActionResult(false, "worker_error", ex.GetType().Name), statusCode: StatusCodes.Status500InternalServerError); }
});

app.MapPost("/v1/egress", async (HttpContext context, ConnectorEgressRequest request, CancellationToken cancellationToken) =>
{
    var token = context.Request.Headers["X-Gram-Execution-Token"].FirstOrDefault() ?? "";
    if (!ValidateExecutionToken(secret, token, request.PackageId, request.UserId, request.ConnectionId)) return Results.Unauthorized();
    var packageRoot = Path.GetFullPath(Path.Combine(installedRoot, request.PackageId));
    if (!packageRoot.StartsWith(installedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return Results.BadRequest(new ConnectorEgressResponse(false, 400, "", "Invalid package path."));
    var manifestPath = Path.Combine(packageRoot, "connector.json");
    if (!File.Exists(manifestPath)) return Results.NotFound(new ConnectorEgressResponse(false, 404, "", "Manifest not found."));
    var manifest = JsonSerializer.Deserialize<ConnectorManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken), BackendJson.Options);
    if (manifest?.ExecutionPolicy is null) return Results.BadRequest(new ConnectorEgressResponse(false, 400, "", "Execution policy missing."));
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)) return Results.BadRequest(new ConnectorEgressResponse(false, 400, "", "Invalid URL."));
    var (policyError, pinnedAddress) = await ConnectorEgressPolicy.ValidateAndResolveAsync(uri, manifest.ExecutionPolicy.AllowedDomains, cancellationToken);
    if (policyError != null) return Results.Json(new ConnectorEgressResponse(false, 403, "", policyError), statusCode: 403);
    if (request.Method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE")) return Results.BadRequest(new ConnectorEgressResponse(false, 400, "", "HTTP method is not allowed."));

    using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
    if (request.Body != null) message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
    foreach (var binding in manifest.CredentialBindings ?? [])
    {
        var value = await credentialVault.ResolveAsync(new ConnectorCredentialReference(request.UserId, manifest.Id, request.ConnectionId, binding.Name), manifest.CredentialNames ?? [], cancellationToken);
        if (!string.IsNullOrWhiteSpace(value)) message.Headers.TryAddWithoutValidation(binding.HeaderName, binding.Prefix + value);
    }
    using var handler = ConnectorEgressPolicy.CreateSecureHandler(manifest.ExecutionPolicy.AllowedDomains, pinnedAddress!);
    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(Math.Clamp(manifest.ExecutionPolicy.TimeoutSeconds, 1, 300)) };
    using var response = await client.SendAsync(message, cancellationToken);
    if ((int)response.StatusCode is >= 300 and < 400)
        return Results.Ok(new ConnectorEgressResponse(false, (int)response.StatusCode, "", "Redirects require explicit destination revalidation."));
    var body = await ReadBoundedContentAsync(response.Content, manifest.ExecutionPolicy.MaxOutputBytes, cancellationToken);
    return Results.Ok(new ConnectorEgressResponse(response.IsSuccessStatusCode, (int)response.StatusCode, body));
});

await app.RunAsync();

static void SetOwnerOnly(string path)
{
    if (OperatingSystem.IsWindows()) return;
    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}

static string CreateExecutionToken(string secret, string packageId, string userId, string connectionId, DateTimeOffset expires)
{
    var payload = $"{packageId}|{userId}|{connectionId}|{expires.ToUnixTimeSeconds()}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) + "." + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
}

static bool ValidateExecutionToken(string secret, string token, string packageId, string userId, string connectionId)
{
    var parts = token.Split('.', 2);
    if (parts.Length != 2) return false;
    string payload;
    try { payload = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])); } catch { return false; }
    var fields = payload.Split('|');
    if (fields.Length != 4 || fields[0] != packageId || fields[1] != userId || fields[2] != connectionId || !long.TryParse(fields[3], out var expiry) || DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow) return false;
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[1].ToUpperInvariant()));
}

static async Task<(string Text, bool Truncated)> ReadBoundedAsync(StreamReader reader, int maxCharacters, CancellationToken cancellationToken)
{
    var buffer = new char[4096];
    var output = new StringBuilder(Math.Min(maxCharacters, 64 * 1024));
    var truncated = false;
    while (true)
    {
        var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
        if (read == 0) break;
        if (output.Length < maxCharacters)
        {
            var take = Math.Min(read, maxCharacters - output.Length);
            output.Append(buffer, 0, take);
            if (take < read) truncated = true;
        }
        else truncated = true;
    }
    return (output.ToString(), truncated);
}

static async Task<string> ReadBoundedContentAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
{
    await using var stream = await content.ReadAsStreamAsync(cancellationToken);
    await using var output = new MemoryStream();
    var buffer = new byte[8192];
    var truncated = false;
    while (true)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
        if (read == 0) break;
        if (output.Length < maxBytes)
        {
            var take = (int)Math.Min(read, maxBytes - output.Length);
            await output.WriteAsync(buffer.AsMemory(0, take), cancellationToken);
            if (take < read) truncated = true;
        }
        else truncated = true;
    }
    var text = Encoding.UTF8.GetString(output.ToArray());
    return truncated ? text + "…" : text;
}
