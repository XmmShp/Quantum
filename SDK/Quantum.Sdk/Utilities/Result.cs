namespace Quantum.Sdk.Utilities;

/// <summary>
/// 表示一个操作的结果，包含成功状态、值和消息
/// </summary>
/// <param name="IsSuccess">操作是否成功</param>
/// <param name="Value">结果值</param>
/// <param name="Message">结果消息</param>
public record Result(bool IsSuccess, object? Value, string Message)
{
    /// <summary>
    /// 创建一个成功的结果
    /// </summary>
    /// <param name="message">可选的成功消息</param>
    /// <returns>成功的结果实例</returns>
    public static Result Success(string message = "") => new(true, default, message);

    /// <summary>
    /// 创建一个带有指定值的成功结果
    /// </summary>
    /// <typeparam name="T">结果值的类型</typeparam>
    /// <param name="value">结果值</param>
    /// <param name="message">可选的成功消息</param>
    /// <returns>成功的泛型结果实例</returns>
    public static Result<T> Success<T>(T value, string message = "") => new(true, value, message);

    /// <summary>
    /// 创建一个失败的结果
    /// </summary>
    /// <param name="message">失败消息</param>
    /// <returns>失败的结果实例</returns>
    public static Result Failure(string message) => new(false, default, message);
}

/// <summary>
/// 表示一个带有类型T的操作结果
/// </summary>
/// <typeparam name="T">结果值的类型</typeparam>
/// <param name="IsSuccess">操作是否成功</param>
/// <param name="Value">类型为T的结果值</param>
/// <param name="Message">结果消息</param>
public record Result<T>(bool IsSuccess, T? Value, string Message)
{
    /// <summary>
    /// 将泛型结果转换为非泛型结果
    /// </summary>
    /// <param name="result">要转换的泛型结果</param>
    public static implicit operator Result(Result<T> result) => new(result.IsSuccess, result.Value, result.Message);

    /// <summary>
    /// 将非泛型结果转换为泛型结果
    /// </summary>
    /// <param name="result">要转换的非泛型结果</param>
    public static implicit operator Result<T>(Result result)
    {
        if (!result.IsSuccess)
        {
            return new Result<T>(result.IsSuccess, default, result.Message);
        }

        return result.Value is not T value
            ? new Result<T>(false, default, result.Message)
            : new Result<T>(result.IsSuccess, value, result.Message);
    }
}
