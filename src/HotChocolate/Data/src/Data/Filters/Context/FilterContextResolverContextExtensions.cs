using HotChocolate.Data.Filters.Expressions;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using static HotChocolate.Data.Filters.Expressions.QueryableFilterProvider;

namespace HotChocolate.Data.Filters;

/// <summary>
/// Common extension of <see cref="IResolverContext" /> for filter context
/// </summary>
public static class FilterContextResolverContextExtensions
{
    /// <summary>
    /// Creates a <see cref="FilterContext" /> from the filter argument.
    /// </summary>
    public static IFilterContext? GetFilterContext(this IResolverContext context)
    {
        IObjectField field = context.Selection.Field;
        if (!field.ContextData.TryGetValue(ContextArgumentNameKey, out var argumentNameObj) ||
            argumentNameObj is not NameString argumentName)
        {
            return null;
        }

        IInputField argument = context.Selection.Field.Arguments[argumentName];
        IValueNode filter = context.LocalContextData.ContainsKey(ContextValueNodeKey) &&
            context.LocalContextData[ContextValueNodeKey] is IValueNode node
                ? node
                : context.ArgumentLiteral<IValueNode>(argumentName);

        if (argument.Type is not IFilterInputType filterInput)
        {
            return null;
        }

        FilterContext filterContext =
            new(context, filterInput, filter, context.Service<InputParser>());

        // disable the execution of filtering by default
        filterContext.EnableFilterExecution(false);

        return filterContext;
    }
}
