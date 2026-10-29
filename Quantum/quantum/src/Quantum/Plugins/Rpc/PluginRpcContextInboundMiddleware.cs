using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;

namespace Quantum.Plugins;

internal sealed class PluginRpcContextInboundMiddleware : IRequestInboundMiddleware
{
    internal Context CallerContext { get; set; } = Context.Empty;

    public TopologyComparison Compare(IRequestInboundMiddleware other)
        => other is TracingInboundMiddleware ? TopologyComparison.Before : TopologyComparison.DoesNotMatter;

    public async ValueTask InvokeAsync(RequestInboundContext context, object request, RequestHandlerDelegate next, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = new Dictionary<object, object?>(context.Items);
        foreach (var item in CallerContext.Items)
        {
            items[item.Key] = item.Value;
        }

        await next((RequestInboundContext)context.WithItems(items), request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
