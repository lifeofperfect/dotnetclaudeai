namespace TodoApi.Infrastructure;

// Hardening headers for a JSON-only API, applied to every response including errors.
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting: survives UseExceptionHandler clearing headers (same reason as AIAgentHeaderMiddleware).
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers[HeaderNames.XFrameOptions] = "DENY";
            // The API never serves HTML, so nothing may load, run or frame it.
            headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; frame-ancestors 'none'";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            // Responses contain per-user data; keep them out of shared/browser caches.
            headers[HeaderNames.CacheControl] = "no-store";
            return Task.CompletedTask;
        });
        return next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
