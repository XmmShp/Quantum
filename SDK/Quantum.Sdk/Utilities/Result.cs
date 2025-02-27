namespace Quantum.Sdk.Utilities;

public record Result(bool IsSuccess, object? Value, string Message)
{
    public static Result Success(string message = "") => new(true, default, message);
    public static Result<T> Success<T>(T value, string message = "") => new(true, value, message);
    public static Result Failure(string message) => new(false, default, message);
}

public record Result<T>(bool IsSuccess, T? Value, string Message)
{
    public static implicit operator Result(Result<T> result) => new(result.IsSuccess, result.Value, result.Message);
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
