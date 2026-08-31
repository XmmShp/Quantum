using System.Globalization;

namespace Quantum.Domain.Plugins;

public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private SemanticVersion(
        int major,
        int minor,
        int patch,
        IReadOnlyList<string> preRelease,
        string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        BuildMetadata = buildMetadata;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> PreRelease { get; }

    public string? BuildMetadata { get; }

    public static SemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException($"'{value}' is not a valid semantic version.");
        }

        return version;
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var buildSeparator = normalized.IndexOf('+', StringComparison.Ordinal);
        var build = buildSeparator >= 0 ? normalized[(buildSeparator + 1)..] : null;
        if (buildSeparator >= 0)
        {
            normalized = normalized[..buildSeparator];
        }

        var preReleaseSeparator = normalized.IndexOf('-', StringComparison.Ordinal);
        var preReleaseValue = preReleaseSeparator >= 0 ? normalized[(preReleaseSeparator + 1)..] : null;
        if (preReleaseSeparator >= 0)
        {
            normalized = normalized[..preReleaseSeparator];
        }

        var numericParts = normalized.Split('.');
        var minor = 0;
        var patch = 0;
        if (numericParts.Length is < 1 or > 3
            || !TryParseNumber(numericParts[0], out var major)
            || (numericParts.Length > 1 && !TryParseNumber(numericParts[1], out minor))
            || (numericParts.Length > 2 && !TryParseNumber(numericParts[2], out patch)))
        {
            return false;
        }

        var preRelease = string.IsNullOrEmpty(preReleaseValue)
            ? []
            : preReleaseValue.Split('.', StringSplitOptions.None);
        if (preRelease.Any(static part => !IsValidIdentifier(part, numericOnlyLeadingZeroRule: true))
            || (build is not null && build.Split('.').Any(static part => !IsValidIdentifier(part, false))))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease, build);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var numericComparison = Major.CompareTo(other.Major);
        if (numericComparison == 0)
        {
            numericComparison = Minor.CompareTo(other.Minor);
        }

        if (numericComparison == 0)
        {
            numericComparison = Patch.CompareTo(other.Patch);
        }

        if (numericComparison != 0)
        {
            return numericComparison;
        }

        if (PreRelease.Count == 0 || other.PreRelease.Count == 0)
        {
            return PreRelease.Count == other.PreRelease.Count ? 0 : PreRelease.Count == 0 ? 1 : -1;
        }

        for (var index = 0; index < Math.Min(PreRelease.Count, other.PreRelease.Count); index++)
        {
            var left = PreRelease[index];
            var right = other.PreRelease[index];
            var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            var comparison = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric != rightNumeric
                    ? leftNumeric ? -1 : 1
                    : string.Compare(left, right, StringComparison.Ordinal);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }

    public bool Equals(SemanticVersion? other)
        => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj)
        => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(Major);
        hashCode.Add(Minor);
        hashCode.Add(Patch);
        foreach (var identifier in PreRelease)
        {
            hashCode.Add(identifier, StringComparer.Ordinal);
        }

        return hashCode.ToHashCode();
    }

    public override string ToString()
    {
        var value = $"{Major}.{Minor}.{Patch}";
        if (PreRelease.Count > 0)
        {
            value += $"-{string.Join('.', PreRelease)}";
        }

        return BuildMetadata is null ? value : $"{value}+{BuildMetadata}";
    }

    private static bool TryParseNumber(string value, out int number)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number)
            && (value == "0" || !value.StartsWith('0'));

    private static bool IsValidIdentifier(string value, bool numericOnlyLeadingZeroRule)
    {
        if (string.IsNullOrEmpty(value) || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            return false;
        }

        return !numericOnlyLeadingZeroRule
            || !value.All(char.IsAsciiDigit)
            || value == "0"
            || !value.StartsWith('0');
    }
}
