namespace Gram.Blazor.Shared;

public sealed class BackendAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true)
        {
            var header = http.Request.Headers.Authorization.FirstOrDefault();
            var gatewayToken = Environment.GetEnvironmentVariable("GRAM_GATEWAY_TOKEN")
                ?? Environment.GetEnvironmentVariable("GRAM_ACCESS_TOKEN");

            if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                // In local development without gateway token, allow localhost loopback client
                if (string.IsNullOrWhiteSpace(gatewayToken) && (http.Request.Host.Host == "localhost" || http.Request.Host.Host == "127.0.0.1"))
                {
                    return await next(context);
                }
                return Results.Unauthorized();
            }

            var token = header["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(gatewayToken) && string.Equals(token, gatewayToken, StringComparison.Ordinal))
            {
                return await next(context);
            }

            var auth = http.RequestServices.GetRequiredService<IAuthService>();
            if (!await auth.ValidateSessionTokenAsync(token)) return Results.Unauthorized();
        }
        return await next(context);
    }
}
