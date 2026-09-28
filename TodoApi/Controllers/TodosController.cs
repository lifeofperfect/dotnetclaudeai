namespace TodoApi.Controllers;

// Authorization is required for all controllers (see MapControllers().RequireAuthorization() in Program.cs).
// Every query is scoped to the current user; another user's todo is reported as 404, not 403,
// so ids can't be probed for existence.
[ApiController]
[Route("api/todos")]
public class TodosController(TodoDbContext db, TimeProvider clock) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user has no NameIdentifier claim.");

    private IQueryable<TodoItem> MyTodos
    {
        get
        {
            var userId = UserId;
            return db.Todos.Where(t => t.OwnerId == userId);
        }
    }

    // GET api/todos?status=InProgress&priority=High&search=report&sortBy=priority&descending=true&page=1&pageSize=20
    [HttpGet]
    public async Task<ActionResult<PagedResponse<TodoResponse>>> GetAll([FromQuery] TodoListQuery q)
    {
        var query = MyTodos.AsNoTracking();

        if (q.Status is not null) query = query.Where(t => t.Status == q.Status);
        if (q.Priority is not null) query = query.Where(t => t.Priority == q.Priority);
        if (!string.IsNullOrWhiteSpace(q.Search))
            query = query.Where(t => t.Title.Contains(q.Search) || (t.Description != null && t.Description.Contains(q.Search)));

        // ThenBy(Id) keeps the order stable so items don't repeat or go missing across pages.
        query = (q.SortBy?.ToLowerInvariant(), q.Descending) switch
        {
            ("priority", false) => query.OrderBy(t => t.Priority).ThenBy(t => t.Id),
            ("priority", true) => query.OrderByDescending(t => t.Priority).ThenBy(t => t.Id),
            ("status", false) => query.OrderBy(t => t.Status).ThenBy(t => t.Id),
            ("status", true) => query.OrderByDescending(t => t.Status).ThenBy(t => t.Id),
            ("duedate", false) => query.OrderBy(t => t.DueDate).ThenBy(t => t.Id),
            ("duedate", true) => query.OrderByDescending(t => t.DueDate).ThenBy(t => t.Id),
            (_, true) => query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id),
            _ => query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id),
        };

        var total = await query.CountAsync();
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();

        return new PagedResponse<TodoResponse>(
            items.Select(TodoResponse.From).ToList(),
            q.Page, q.PageSize, total, (int)Math.Ceiling(total / (double)q.PageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TodoResponse>> GetById(int id)
    {
        var todo = await MyTodos.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
        if (todo is null) return NotFound();

        SetETag(todo);
        return TodoResponse.From(todo);
    }

    [HttpPost]
    public async Task<ActionResult<TodoResponse>> Create(CreateTodoRequest request)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var todo = new TodoItem
        {
            OwnerId = UserId,
            Title = request.Title.Trim(),
            Description = request.Description,
            Priority = request.Priority,
            DueDate = request.DueDate,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Todos.Add(todo);
        await db.SaveChangesAsync();

        SetETag(todo);
        return CreatedAtAction(nameof(GetById), new { id = todo.Id }, TodoResponse.From(todo));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TodoResponse>> Update(int id, UpdateTodoRequest request)
    {
        var (todo, error) = await LoadForWriteAsync(id);
        if (error is not null) return error;

        todo!.Title = request.Title.Trim();
        todo.Description = request.Description;
        todo.Priority = request.Priority;
        todo.DueDate = request.DueDate;
        SetStatus(todo, request.Status);

        await db.SaveChangesAsync();
        SetETag(todo);
        return TodoResponse.From(todo);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<TodoResponse>> UpdateStatus(int id, UpdateStatusRequest request)
    {
        var (todo, error) = await LoadForWriteAsync(id);
        if (error is not null) return error;

        SetStatus(todo!, request.Status);
        await db.SaveChangesAsync();
        SetETag(todo!);
        return TodoResponse.From(todo!);
    }

    [HttpPatch("{id:int}/priority")]
    public async Task<ActionResult<TodoResponse>> UpdatePriority(int id, UpdatePriorityRequest request)
    {
        var (todo, error) = await LoadForWriteAsync(id);
        if (error is not null) return error;

        todo!.Priority = request.Priority;
        todo.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        SetETag(todo);
        return TodoResponse.From(todo);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var (todo, error) = await LoadForWriteAsync(id);
        if (error is not null) return error;

        db.Todos.Remove(todo!);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Loads the caller's todo and enforces the If-Match precondition:
    // 404 if missing, 428 if If-Match is absent, 412 if it doesn't match the current version.
    // RowVersion is a concurrency token, so a write that races in between this check and
    // SaveChanges still fails (DbUpdateConcurrencyException -> 412 in GlobalExceptionHandler).
    private async Task<(TodoItem? Todo, ActionResult? Error)> LoadForWriteAsync(int id)
    {
        var todo = await MyTodos.SingleOrDefaultAsync(t => t.Id == id);
        if (todo is null) return (null, NotFound());

        var ifMatch = Request.GetTypedHeaders().IfMatch;
        if (ifMatch.Count == 0)
            return (null, Problem(statusCode: StatusCodes.Status428PreconditionRequired,
                title: "If-Match header is required",
                detail: "Send the ETag from your last read of this todo in the If-Match header."));

        var current = ETagFor(todo);
        var matches = ifMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: true));
        if (!matches)
            return (null, Problem(statusCode: StatusCodes.Status412PreconditionFailed,
                title: "The todo was modified by another request",
                detail: "Fetch the latest version and retry with its ETag."));

        return (todo, null);
    }

    // Same GUID format as TodoResponse.RowVersion, so clients can build If-Match from either.
    private static EntityTagHeaderValue ETagFor(TodoItem todo) => new($"\"{todo.RowVersion}\"");

    private void SetETag(TodoItem todo) => Response.GetTypedHeaders().ETag = ETagFor(todo);

    // Keeps CompletedAt in sync with the status; also stamps UpdatedAt.
    private void SetStatus(TodoItem todo, TodoStatus status)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (status == TodoStatus.Done && todo.Status != TodoStatus.Done)
            todo.CompletedAt = now;
        else if (status != TodoStatus.Done)
            todo.CompletedAt = null;

        todo.Status = status;
        todo.UpdatedAt = now;
    }
}
