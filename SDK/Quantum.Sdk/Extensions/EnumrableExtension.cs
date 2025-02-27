using System.Reflection;

namespace Quantum.Infrastructure.Extensions;

public static class EnumrableExtension
{
    public static IEnumerable<Assembly> GetAssemblies<T>(this IEnumerable<T> objs)
    {
        foreach (var obj in objs)
        {
            if (obj?.GetType() is { } type)
                yield return type.Assembly;
        }
    }
}
