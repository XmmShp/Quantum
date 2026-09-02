using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Quantum.Plugins;

internal sealed class PluginRpcSerializer : IDisposable
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
    private int _disposed;

    public JsonElement Serialize(object? value, Type inputType)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return JsonSerializer.SerializeToElement(value, inputType, _options);
    }

    public object? Deserialize(JsonElement payload, Type returnType)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return payload.Deserialize(returnType, _options);
    }

    public T? Deserialize<T>(JsonElement payload)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return payload.Deserialize<T>(_options);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ClearJsonSerializerCaches(_options);
        ClearJsonMemberAccessorCaches(null);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ClearCaches")]
    private static extern void ClearJsonSerializerCaches(JsonSerializerOptions options);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ClearMemberAccessorCaches")]
    private static extern void ClearJsonMemberAccessorCaches(DefaultJsonTypeInfoResolver? resolver);
}
