using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace StreamForge.Upload.Api.Infrastructure;

/// <summary>
/// Disables ASP.NET Core form model binding so multipart bodies can be consumed as streams.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
