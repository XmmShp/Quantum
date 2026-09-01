using System.Text.RegularExpressions;

namespace Quantum.Plugins;

public readonly partial record struct PluginId
{
    public PluginId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim().ToLowerInvariant();
        if (!ValidId().IsMatch(normalized))
        {
            throw new ArgumentException(
                "Plugin id must contain only lowercase letters, numbers, dots, underscores, or hyphens.",
                nameof(value));
        }

        if (string.Equals(normalized, "disabled", StringComparison.Ordinal))
        {
            throw new ArgumentException("Plugin id 'disabled' is reserved by the host.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,126}[a-z0-9])?$")]
    private static partial Regex ValidId();
}
