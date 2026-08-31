using System.Text.RegularExpressions;
using NOF.Domain;

namespace Quantum.ExtensionMarket.Domain;

public sealed class PluginListing
{
    private static readonly Regex PluginIdPattern = new(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private PluginListing()
    {
    }

    private PluginListing(
        PluginListingId id,
        string pluginId,
        string name,
        string description,
        MarketUserId authorUserId,
        string[] tags,
        DateTime createdAtUtc)
    {
        Id = id;
        PluginId = NormalizePluginId(pluginId);
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        AuthorUserId = authorUserId;
        Tags = NormalizeTags(tags);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public PluginListingId Id { get; private set; }
    public string PluginId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public MarketUserId AuthorUserId { get; private set; }
    public string[] Tags { get; private set; } = [];
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static PluginListing Create(
        string pluginId,
        string name,
        string description,
        MarketUserId authorUserId,
        IEnumerable<string>? tags = null,
        IIdGenerator? idGenerator = null,
        TimeProvider? timeProvider = null)
        => new(
            PluginListingId.New(idGenerator.OrDefault()),
            pluginId,
            name,
            description,
            authorUserId,
            NormalizeTags(tags),
            timeProvider.OrDefault().GetUtcNow().UtcDateTime);

    public void Update(
        string name,
        string description,
        IEnumerable<string>? tags,
        TimeProvider? timeProvider = null)
    {
        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        Tags = NormalizeTags(tags);
        Touch(timeProvider);
    }

    public void Touch(TimeProvider? timeProvider = null)
        => UpdatedAtUtc = timeProvider.OrDefault().GetUtcNow().UtcDateTime;

    public static string NormalizePluginId(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var normalized = pluginId.Trim().ToLowerInvariant();
        if (normalized.Length > 200 || !PluginIdPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Plugin id must be lower-case dot/dash-separated alphanumeric segments.",
                nameof(pluginId));
        }

        return normalized;
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException("Plugin name cannot exceed 200 characters.", nameof(name));
        }

        return normalized;
    }

    private static string NormalizeDescription(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var normalized = description.Trim();
        if (normalized.Length > 4000)
        {
            throw new ArgumentException("Description cannot exceed 4000 characters.", nameof(description));
        }

        return normalized;
    }

    private static string[] NormalizeTags(IEnumerable<string>? tags)
    {
        var normalized = (tags ?? [])
            .Select(static tag => tag.Trim().ToLowerInvariant())
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length > 20 || normalized.Any(static tag => tag.Length > 50 || tag.Any(char.IsControl)))
        {
            throw new ArgumentException("At most 20 tags of 50 visible characters are allowed.", nameof(tags));
        }

        return normalized;
    }
}
