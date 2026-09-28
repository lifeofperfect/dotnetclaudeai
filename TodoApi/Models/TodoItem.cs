namespace TodoApi.Models;

public enum TodoStatus
{
    Todo,
    InProgress,
    Done,
    Cancelled
}

public enum TodoPriority
{
    Low,
    Medium,
    High,
    Critical
}

public class TodoItem
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public TodoStatus Status { get; set; } = TodoStatus.Todo;
    public TodoPriority Priority { get; set; } = TodoPriority.Medium;
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
