namespace TodoApi.Data;

// Development only (called from Program.cs). Creates a demo account that owns the sample todos.
public static class SeedData
{
    public const string DemoEmail = "demo@example.com";
    public const string DemoPassword = "Demo-Passw0rd!";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        if (db.Todos.Any()) return;

        var demo = new AppUser { UserName = DemoEmail, Email = DemoEmail, EmailConfirmed = true };
        var created = await users.CreateAsync(demo, DemoPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException("Seeding demo user failed: " + string.Join("; ", created.Errors.Select(e => e.Description)));

        var todos = new[]
        {
            Todo("Fix login bug on mobile", "Users on iOS get logged out after 5 minutes.",
                TodoStatus.InProgress, TodoPriority.Critical, due: now.AddDays(1), created: now.AddDays(-3)),
            Todo("Write quarterly report", "Include Q3 revenue numbers and the forecast for Q4.",
                TodoStatus.Todo, TodoPriority.High, due: now.AddDays(7), created: now.AddDays(-2)),
            Todo("Prepare sprint demo", "Slides plus a live walkthrough of the new dashboard.",
                TodoStatus.Todo, TodoPriority.High, due: now.AddDays(3), created: now.AddDays(-1)),
            Todo("Review pull request #142", null,
                TodoStatus.InProgress, TodoPriority.Medium, due: now.AddDays(2), created: now.AddHours(-20)),
            Todo("Update project dependencies", "Bump EF Core and ASP.NET Core to the latest patch.",
                TodoStatus.Todo, TodoPriority.Medium, due: null, created: now.AddDays(-5)),
            Todo("Book dentist appointment", null,
                TodoStatus.Todo, TodoPriority.Low, due: now.AddDays(14), created: now.AddDays(-4)),
            Todo("Set up CI pipeline", "Build and test on every push to main.",
                TodoStatus.Done, TodoPriority.High, due: now.AddDays(-2), created: now.AddDays(-10), completed: now.AddDays(-3)),
            Todo("Buy groceries", "Milk, eggs, bread, coffee.",
                TodoStatus.Done, TodoPriority.Low, due: null, created: now.AddDays(-2), completed: now.AddDays(-1)),
            Todo("Migrate to new logging library", "Decided to keep the current one.",
                TodoStatus.Cancelled, TodoPriority.Medium, due: null, created: now.AddDays(-12)),
            Todo("Renew SSL certificate", "Current certificate expires at the end of the month.",
                TodoStatus.Todo, TodoPriority.Critical, due: now.AddDays(-1), created: now.AddDays(-8)),
        };

        foreach (var todo in todos) todo.OwnerId = demo.Id;
        db.Todos.AddRange(todos);
        await db.SaveChangesAsync();
    }

    private static TodoItem Todo(string title, string? description, TodoStatus status, TodoPriority priority,
        DateTime? due, DateTime created, DateTime? completed = null) => new()
    {
        OwnerId = "", // assigned to the demo user in InitializeAsync
        Title = title,
        Description = description,
        Status = status,
        Priority = priority,
        DueDate = due,
        CreatedAt = created,
        UpdatedAt = completed ?? created,
        CompletedAt = completed,
    };
}
