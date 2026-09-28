namespace TodoApi.Infrastructure;

// Runs the registered IValidator<T> for every action argument and returns the same
// 400 ValidationProblemDetails shape that [ApiController] produces for binding errors.
public class FluentValidationFilter(IOptions<ApiBehaviorOptions> apiBehavior) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            foreach (var error in result.Errors)
                context.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        }

        if (!context.ModelState.IsValid)
        {
            context.Result = apiBehavior.Value.InvalidModelStateResponseFactory(context);
            return;
        }

        await next();
    }
}
