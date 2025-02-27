using System.Reflection;

namespace Quantum.Infrastructure.Extensions;

/// <summary>
/// 可枚举类型扩展方法类
/// </summary>
public static class EnumrableExtension
{
    /// <summary>
    /// 获取对象集合中每个对象的程序集
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="objs">对象集合</param>
    /// <returns>程序集集合</returns>
    public static IEnumerable<Assembly> GetAssemblies<T>(this IEnumerable<T> objs)
    {
        foreach (var obj in objs)
        {
            if (obj?.GetType() is { } type)
                yield return type.Assembly;
        }
    }
}
