namespace TodoApi.Validators;

public static class TodoRules
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    public static IRuleBuilderOptions<T, string> ValidTitle<T>(this IRuleBuilder<T, string> rule) => rule
        .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Title is required.")
        .Must(t => t is null || t.Trim().Length <= TitleMaxLength)
        .WithMessage($"Title must be {TitleMaxLength} characters or fewer.");

    public static IRuleBuilderOptions<T, TEnum> ValidEnum<T, TEnum>(this IRuleBuilder<T, TEnum> rule) where TEnum : struct, Enum => rule
        .IsInEnum()
        .WithMessage($"{{PropertyName}} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");

    public static IRuleBuilderOptions<T, string?> ValidDescription<T>(this IRuleBuilder<T, string?> rule) => rule
        .MaximumLength(DescriptionMaxLength)
        .WithMessage($"Description must be {DescriptionMaxLength} characters or fewer.");
}

public class CreateTodoRequestValidator : AbstractValidator<CreateTodoRequest>
{
    public CreateTodoRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.Title).ValidTitle();
        RuleFor(x => x.Description).ValidDescription();
        RuleFor(x => x.Priority).ValidEnum();

        // New todos can't be created already overdue.
        RuleFor(x => x.DueDate)
            .Must(due => due!.Value.ToUniversalTime() > clock.GetUtcNow().UtcDateTime)
            .When(x => x.DueDate.HasValue)
            .WithMessage("Due date must be in the future.");
    }
}

public class UpdateTodoRequestValidator : AbstractValidator<UpdateTodoRequest>
{
    public UpdateTodoRequestValidator()
    {
        RuleFor(x => x.Title).ValidTitle();
        RuleFor(x => x.Description).ValidDescription();
        RuleFor(x => x.Status).ValidEnum();
        RuleFor(x => x.Priority).ValidEnum();
        // No future-date rule on update: existing todos may legitimately be overdue.
    }
}

public class TodoListQueryValidator : AbstractValidator<TodoListQuery>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 100;
    public static readonly string[] SortFields = ["createdat", "priority", "status", "duedate"];

    public TodoListQueryValidator()
    {
        RuleFor(x => x.Status!.Value).ValidEnum().OverridePropertyName("Status").When(x => x.Status.HasValue);
        RuleFor(x => x.Priority!.Value).ValidEnum().OverridePropertyName("Priority").When(x => x.Priority.HasValue);
        RuleFor(x => x.Search).MaximumLength(SearchMaxLength);
        RuleFor(x => x.SortBy)
            .Must(s => SortFields.Contains(s!.ToLowerInvariant()))
            .When(x => x.SortBy is not null)
            .WithMessage($"SortBy must be one of: {string.Join(", ", SortFields)}.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}

public class UpdateStatusRequestValidator : AbstractValidator<UpdateStatusRequest>
{
    public UpdateStatusRequestValidator() => RuleFor(x => x.Status).ValidEnum();
}

public class UpdatePriorityRequestValidator : AbstractValidator<UpdatePriorityRequest>
{
    public UpdatePriorityRequestValidator() => RuleFor(x => x.Priority).ValidEnum();
}
