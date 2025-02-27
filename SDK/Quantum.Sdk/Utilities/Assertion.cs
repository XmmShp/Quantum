namespace Quantum.Sdk.Utilities;

/// <summary>
/// 断言工具类，用于参数验证
/// </summary>
public static class Assertion
{
    /// <summary>
    /// 验证对象不为null
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="value">要验证的值</param>
    /// <param name="paramName">参数名称，可选</param>
    /// <returns>非null的值</returns>
    /// <exception cref="ArgumentNullException">当值为null时抛出</exception>
    public static T NotNull<T>(T? value, string? paramName = null) where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName ?? nameof(value), "Value cannot be null");
        }
        return value;
    }

    /// <summary>
    /// 验证字符串不为null且不为空
    /// </summary>
    /// <param name="value">要验证的字符串</param>
    /// <param name="paramName">参数名称，可选</param>
    /// <returns>非null且非空的字符串</returns>
    /// <exception cref="ArgumentException">当字符串为null或空时抛出</exception>
    public static string NotNullNorEmpty(string? value, string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Value cannot be null or empty", paramName ?? nameof(value));
        }
        return value;
    }
}
