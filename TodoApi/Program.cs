var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(o =>
    {
        o.Filters.Add<FluentValidationFilter>();
        // FluentValidation owns "required" rules; stop MVC from adding its own for non-nullable strings.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await SeedData.InitializeAsync(app.Services);
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
