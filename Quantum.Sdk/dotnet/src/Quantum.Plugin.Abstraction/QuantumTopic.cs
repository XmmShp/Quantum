using NOF.Domain;
using System.Text.RegularExpressions;

namespace Quantum.Plugin.Abstraction;

/// <summary>
/// Identifies a validated Quantum event-bus topic.
/// </summary>
[ValueObjectLength(255, MinimumLength = 1)]
public readonly partial struct QuantumTopic : IValueObject<string>
{
    private const string Pattern = @"^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$";

    /// <summary>
    /// Validates a topic against
    /// <c>^[A-Za-z][A-Za-z0-9_-]*(\.[A-Za-z0-9][A-Za-z0-9_-]*)*$</c>.
    /// </summary>
    /// <exception cref="DomainValidationException">
    /// The topic is empty, contains surrounding whitespace, has empty segments, or contains
    /// characters that are not valid in a topic name.
    /// </exception>
    public static void Validate(string value)
    {
        var match = string.IsNullOrEmpty(value) ? Match.Empty : TopicPattern().Match(value);
        if (!match.Success || match.Length != value.Length)
        {
            throw new DomainValidationException(
                $"A topic must match {Pattern}.");
        }
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex TopicPattern();
}
