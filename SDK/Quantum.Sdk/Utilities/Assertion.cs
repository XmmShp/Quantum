namespace Quantum.Sdk.Utilities;

public static class Assertion
{
    public static T NotNull<T>(T? value, string? paramName = null) where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName ?? nameof(value), "Value cannot be null");
        }
        return value;
    }

    public static string NotNullNorEmpty(string? value, string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Value cannot be null or empty", paramName ?? nameof(value));
        }
        return value;
    }
}
