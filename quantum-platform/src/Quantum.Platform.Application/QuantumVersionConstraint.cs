using System.Text.RegularExpressions;

namespace Quantum.Platform.Application;

public static partial class QuantumVersionConstraint
{
    public static bool Contains(string expression, string quantumVersion)
    {
        if (!TryParseVersion(quantumVersion, out var candidate) || string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var normalized = expression.Trim();
        var compactRange = CompactRangePattern().Match(normalized);
        if (compactRange.Success &&
            TryParseVersion(compactRange.Groups[1].Value, out var compactMinimum) &&
            TryParseVersion(compactRange.Groups[2].Value, out var compactMaximum))
        {
            return candidate >= compactMinimum && candidate <= compactMaximum;
        }

        var spacedRange = SpacedRangePattern().Match(normalized);
        if (spacedRange.Success &&
            TryParseVersion(spacedRange.Groups[1].Value, out var minimum) &&
            TryParseVersion(spacedRange.Groups[2].Value, out var maximum))
        {
            return candidate >= minimum && candidate <= maximum;
        }

        if (normalized.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = normalized[..^2];
            return candidate.ToString().StartsWith(prefix + ".", StringComparison.Ordinal) ||
                   string.Equals(candidate.ToString(), prefix, StringComparison.Ordinal);
        }

        var clauses = normalized.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (clauses.Length > 1 || clauses[0].StartsWithAny(">", "<", "="))
        {
            return clauses.All(clause => MatchesClause(candidate, clause));
        }

        return TryParseVersion(normalized, out var exact) && candidate == exact;
    }

    public static int CompareSemanticVersions(string left, string right)
    {
        if (!TryParseVersion(left, out var leftVersion) || !TryParseVersion(right, out var rightVersion))
        {
            return string.Compare(left, right, StringComparison.Ordinal);
        }

        var comparison = leftVersion.CompareTo(rightVersion);
        if (comparison != 0)
        {
            return comparison;
        }

        var leftPrerelease = GetPrerelease(left);
        var rightPrerelease = GetPrerelease(right);
        if (leftPrerelease is null)
        {
            return rightPrerelease is null ? 0 : 1;
        }

        return rightPrerelease is null ? -1 : string.Compare(leftPrerelease, rightPrerelease, StringComparison.Ordinal);
    }

    private static bool MatchesClause(Version candidate, string clause)
    {
        var (operation, versionText) = clause switch
        {
            var value when value.StartsWith(">=", StringComparison.Ordinal) => (">=", value[2..]),
            var value when value.StartsWith("<=", StringComparison.Ordinal) => ("<=", value[2..]),
            var value when value.StartsWith(">", StringComparison.Ordinal) => (">", value[1..]),
            var value when value.StartsWith("<", StringComparison.Ordinal) => ("<", value[1..]),
            var value when value.StartsWith("=", StringComparison.Ordinal) => ("=", value[1..]),
            _ => ("=", clause)
        };
        if (!TryParseVersion(versionText, out var expected))
        {
            return false;
        }

        return operation switch
        {
            ">=" => candidate >= expected,
            "<=" => candidate <= expected,
            ">" => candidate > expected,
            "<" => candidate < expected,
            _ => candidate == expected
        };
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var withoutMetadata = value.Trim().Split(['-', '+'], 2)[0];
        if (!Version.TryParse(withoutMetadata, out var parsed))
        {
            version = new Version();
            return false;
        }

        version = parsed;
        return true;
    }

    private static string? GetPrerelease(string value)
    {
        var version = value.Split('+', 2)[0];
        var separator = version.IndexOf('-');
        return separator < 0 ? null : version[(separator + 1)..];
    }

    private static bool StartsWithAny(this string value, params string[] prefixes)
        => prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));

    [GeneratedRegex(@"^(\d+(?:\.\d+){1,3})-(\d+(?:\.\d+){1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex CompactRangePattern();

    [GeneratedRegex(@"^(\d+(?:\.\d+){1,3})\s+-\s+(\d+(?:\.\d+){1,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex SpacedRangePattern();
}
