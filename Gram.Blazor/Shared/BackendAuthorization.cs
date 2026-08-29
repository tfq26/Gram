namespace Gram.Blazor.Shared;

public sealed class BackendAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true)
        {
            var header = http.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();
            var token = header["Bearer ".Length..].Trim();
            var auth = http.RequestServices.GetRequiredService<IAuthService>();
            if (!await auth.ValidateSessionTokenAsync(token)) return Results.Unauthorized();
        }
        return await next(context);
    }
}
