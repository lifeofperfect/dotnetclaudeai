namespace TodoApi.Dtos;

public record TodoResponse(
    int Id,
    string Title,
    string? Description,
    TodoStatus Status,
    TodoPriority Priority,
    DateTime? DueDate,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    Guid RowVersion)
{
    public static TodoResponse From(TodoItem t) => new(
        t.Id, t.Title, t.Description, t.Status, t.Priority,
        t.DueDate, t.CreatedAt, t.UpdatedAt, t.CompletedAt, t.RowVersion);
}

public record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

// Validation rules for these requests live in Validators/ (FluentValidation).

// Bound from the query string of GET /api/todos.
public class TodoListQuery
{
    public TodoStatus? Status { get; set; }
    public TodoPriority? Priority { get; set; }
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool Descending { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = TodoListQueryValidator.DefaultPageSize;
}

public record CreateTodoRequest(
    string Title,
    string? Description,
    TodoPriority Priority = TodoPriority.Medium,
    DateTime? DueDate = null);

public record UpdateTodoRequest(
    string Title,
    string? Description,
    TodoStatus Status,
    TodoPriority Priority,
    DateTime? DueDate);

public record UpdateStatusRequest(TodoStatus Status);

public record UpdatePriorityRequest(TodoPriority Priority);
