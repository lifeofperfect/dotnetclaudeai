var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false; // don't advertise "Server: Kestrel"
    // Largest valid body is ~9 KB (200-char title + 2000-char description); leave headroom, reject the rest with 413.
    o.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.Services.AddControllers(o =>
    {
        o.Filters.Add<FluentValidationFilter>();
        // FluentValidation owns "required" rules; stop MVC from adding its own for non-nullable strings.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Hide System.Text.Json messages that name internal .NET types (e.g. "TodoApi.Dtos.CreateTodoRequest").
        o.AllowInputFormatterExceptionMessages = builder.Environment.IsDevelopment();
    });
builder.Services.AddOpenApi();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddDbContext<TodoDbContext>(o => o.UseInMemoryDatabase("Todos"));
builder.Services.AddSingleton(TimeProvider.System);

// Authentication: ASP.NET Core Identity with bearer tokens only (no cookies, so no CSRF surface).
builder.Services.AddAuthentication(IdentityConstants.BearerScheme).AddBearerToken(IdentityConstants.BearerScheme);
builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 12;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<TodoDbContext>()
    .AddApiEndpoints();

builder.Services.AddApiRateLimiting(builder.Configuration);

// CORS: only origins listed in config (none by default, so browsers on other sites are blocked).
// Bearer tokens are sent explicitly, so credentials (cookies) are never allowed.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
    .WithHeaders(HeaderNames.Authorization, HeaderNames.ContentType, HeaderNames.IfMatch)
    .WithExposedHeaders(HeaderNames.ETag, HeaderNames.Location, HeaderNames.RetryAfter)
    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

var app = builder.Build();

app.UseAIAgentHeader();
app.UseSecurityHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await SeedData.InitializeAsync(app.Services);
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseRateLimiter(); // after authentication, so limits are per user where possible
app.UseAuthorization();

app.MapGroup("/api/auth")
    .RequireRateLimiting(RateLimitingOptions.AuthPolicy)
    // Only the bearer scheme is registered; cookie login would throw (500), so reject it up front.
    .AddEndpointFilter(async (ctx, next) =>
    {
        var query = ctx.HttpContext.Request.Query;
        var wantsCookies = (bool.TryParse(query["useCookies"], out var c) && c)
            || (bool.TryParse(query["useSessionCookies"], out var s) && s);
        return wantsCookies
            ? TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Cookie authentication is not supported",
                detail: "Use the bearer access token returned by /api/auth/login.")
            : await next(ctx);
    })
    .MapIdentityApi<AppUser>();

app.MapControllers().RequireAuthorization();

app.Run();
