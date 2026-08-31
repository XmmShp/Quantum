using System.Reflection;
using Microsoft.AspNetCore.Components;
using Quantum.Application.Plugins;

namespace Quantum;

public sealed class BlazorPluginReferenceRelease : IPluginReferenceRelease
{
    private static readonly string[] CacheOwnerTypeNames =
    [
        "Microsoft.AspNetCore.Components.ComponentFactory",
        "Microsoft.AspNetCore.Components.DefaultComponentActivator",
        "Microsoft.AspNetCore.Components.Reflection.ComponentProperties"
    ];

    private static readonly string[] DictionaryCacheTypeNames =
    [
        "Microsoft.AspNetCore.Components.CascadingParameterState",
        "Microsoft.AspNetCore.Components.BindConverter+FormatterDelegateCache",
        "Microsoft.AspNetCore.Components.BindConverter+ParserDelegateCache"
    ];

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        // Let the renderer observe the new catalog and dispose components from the old ALC.
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        var componentsAssembly = typeof(IComponent).Assembly;
        foreach (var typeName in CacheOwnerTypeNames)
        {
            componentsAssembly
                .GetType(typeName, throwOnError: false)
                ?.GetMethod("ClearCache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, null);
        }

        foreach (var typeName in DictionaryCacheTypeNames)
        {
            var type = componentsAssembly.GetType(typeName, throwOnError: false);
            foreach (var field in type?.GetFields(BindingFlags.NonPublic | BindingFlags.Static) ?? [])
            {
                var cache = field.GetValue(null);
                cache?.GetType()
                    .GetMethod(
                        "Clear",
                        BindingFlags.Public | BindingFlags.Instance,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null)?
                    .Invoke(cache, null);
            }
        }
    }
}
