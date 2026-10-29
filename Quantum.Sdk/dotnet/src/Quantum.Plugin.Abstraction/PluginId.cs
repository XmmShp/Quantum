using System.Text.RegularExpressions;
using NOF.Domain;

namespace Quantum.Plugin.Abstraction;

/// <summary>
/// Identifies a normalized Quantum plugin.
/// </summary>
[ValueObjectLength(MaximumLength, MinimumLength = 1)]
public readonly partial struct PluginId : IValueObject<string>, IComparable<PluginId>
{
    public const int MaximumLength = 128;

    private const string Pattern = "^[a-z0-9](?:[a-z0-9._-]{0,126}[a-z0-9])?$";

    public static string Normalize(string value)
        => value?.Trim().ToLowerInvariant() ?? string.Empty;

    public static void Validate(string value)
    {
        var match = ValidId().Match(value);
        if (!match.Success || match.Length != value.Length)
        {
            throw new DomainValidationException(
                $"A plugin id must match {Pattern}.");
        }

        if (string.Equals(value, "disabled", StringComparison.Ordinal))
        {
            throw new DomainValidationException("Plugin id 'disabled' is reserved by the host.");
        }
    }

    public int CompareTo(PluginId other)
        => string.Compare((string)this, (string)other, StringComparison.Ordinal);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex ValidId();
}
