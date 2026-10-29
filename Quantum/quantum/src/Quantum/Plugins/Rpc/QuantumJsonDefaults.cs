using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Quantum.Plugins;

internal static class QuantumJsonDefaults
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Quantum configures application-wide NOF defaults before any host or plugin JSON use.")]
    internal static void Initialize()
    {
        JsonSerializerOptions.ConfigureNOFJsonSerializerOptions(options =>
        {
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            options.RespectRequiredConstructorParameters = true;
            options.RespectNullableAnnotations = true;
        });
    }

    // Keep collectible plugin metadata out of the process-wide NOF resolver/cache.
    internal static JsonSerializerOptions CreatePluginOptions() => new(JsonSerializerOptions.NOF)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}
