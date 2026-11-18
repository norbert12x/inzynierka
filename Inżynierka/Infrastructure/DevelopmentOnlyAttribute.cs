using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inżynierka.Infrastructure;

// Narzędzia redakcyjne (/api/dev/...) działają tylko w Development; poza nim odpowiadają 404, jakby ich nie było.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DevelopmentOnlyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            context.Result = new NotFoundObjectResult(new ProblemDetails { Status = 404, Title = "Nie znaleziono." });
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
