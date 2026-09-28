namespace TodoApi.Infrastructure;

// Bound from the "RateLimiting" config section.
public class RateLimitingOptions
{
    public const string AuthPolicy = "auth";

    // Every request: per signed-in user, or per IP for anonymous callers.
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 60;

    // Extra, stricter limit on /api/auth/* (login, register, password reset), per IP, against brute force.
    public int AuthPermitLimit { get; set; } = 10;
    public int AuthWindowSeconds { get; set; } = 60;
}

public static class RateLimitingExtensions
{
    // Note: behind a reverse proxy, RemoteIpAddress is the proxy's IP unless UseForwardedHeaders is configured.
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        return services.AddRateLimiter(o =>
        {
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId ? $"user:{userId}" : $"ip:{ClientIp(ctx)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                    }));

            o.AddPolicy(RateLimitingOptions.AuthPolicy, ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"ip:{ClientIp(ctx)}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(options.AuthWindowSeconds),
                    }));

            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

                await ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = ctx.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Retry after the time in the Retry-After header.",
                    },
                });
            };
        });
    }

    private static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
