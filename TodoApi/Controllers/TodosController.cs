namespace TodoApi.Controllers;

[ApiController]
[Route("api/todos")]
public class TodosController(TodoDbContext db, TimeProvider clock) : ControllerBase
{
    // GET api/todos?status=InProgress&priority=High&search=report&sortBy=priority&descending=true
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TodoResponse>>> GetAll(
        [FromQuery] TodoStatus? status,
        [FromQuery] TodoPriority? priority,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool descending = false)
    {
        IQueryable<TodoItem> query = db.Todos.AsNoTracking();

        if (status is not null) query = query.Where(t => t.Status == status);
        if (priority is not null) query = query.Where(t => t.Priority == priority);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Title.Contains(search) || (t.Description != null && t.Description.Contains(search)));

        query = (sortBy?.ToLowerInvariant(), descending) switch
        {
            ("priority", false) => query.OrderBy(t => t.Priority),
            ("priority", true) => query.OrderByDescending(t => t.Priority),
            ("status", false) => query.OrderBy(t => t.Status),
            ("status", true) => query.OrderByDescending(t => t.Status),
            ("duedate", false) => query.OrderBy(t => t.DueDate),
            ("duedate", true) => query.OrderByDescending(t => t.DueDate),
            (_, true) => query.OrderByDescending(t => t.CreatedAt),
            _ => query.OrderBy(t => t.CreatedAt),
        };

        var items = await query.ToListAsync();
        return Ok(items.Select(TodoResponse.From));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TodoResponse>> GetById(int id)
    {
        var todo = await db.Todos.FindAsync(id);
        return todo is null ? NotFound() : TodoResponse.From(todo);
    }

    [HttpPost]
    public async Task<ActionResult<TodoResponse>> Create(CreateTodoRequest request)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var todo = new TodoItem
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Priority = request.Priority,
            DueDate = request.DueDate,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Todos.Add(todo);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = todo.Id }, TodoResponse.From(todo));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TodoResponse>> Update(int id, UpdateTodoRequest request)
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null) return NotFound();

        todo.Title = request.Title.Trim();
        todo.Description = request.Description;
        todo.Priority = request.Priority;
        todo.DueDate = request.DueDate;
        SetStatus(todo, request.Status);

        await db.SaveChangesAsync();
        return TodoResponse.From(todo);
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<TodoResponse>> UpdateStatus(int id, UpdateStatusRequest request)
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null) return NotFound();

        SetStatus(todo, request.Status);
        await db.SaveChangesAsync();
        return TodoResponse.From(todo);
    }

    [HttpPatch("{id:int}/priority")]
    public async Task<ActionResult<TodoResponse>> UpdatePriority(int id, UpdatePriorityRequest request)
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null) return NotFound();

        todo.Priority = request.Priority;
        todo.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return TodoResponse.From(todo);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null) return NotFound();

        db.Todos.Remove(todo);
        await db.SaveChangesAsync();
        return NoContent();
    }

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
