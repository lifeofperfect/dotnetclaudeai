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
    DateTime? CompletedAt)
{
    public static TodoResponse From(TodoItem t) => new(
        t.Id, t.Title, t.Description, t.Status, t.Priority,
        t.DueDate, t.CreatedAt, t.UpdatedAt, t.CompletedAt);
}

// Validation rules for these requests live in Validators/ (FluentValidation).

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
