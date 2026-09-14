using System.Collections;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace Quantum.Localization;

public sealed class CultureAwareStringLocalizer<TResource>(CultureService cultureService)
    : IStringLocalizer<TResource>
{
    private static readonly ResourceManager Resources = new(
        typeof(TResource).FullName
            ?? throw new InvalidOperationException("The localization resource type must have a full name."),
        typeof(TResource).Assembly);

    public LocalizedString this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            var value = Resources.GetString(name, cultureService.CurrentCulture);
            return new LocalizedString(
                name,
                value ?? name,
                resourceNotFound: value is null,
                searchedLocation: typeof(TResource).Assembly.FullName);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var localized = this[name];
            return new LocalizedString(
                name,
                string.Format(cultureService.CurrentCulture, localized.Value, arguments),
                localized.ResourceNotFound,
                localized.SearchedLocation);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var resourceSet = Resources.GetResourceSet(
            cultureService.CurrentCulture,
            createIfNotExists: true,
            tryParents: includeParentCultures);
        if (resourceSet is null)
        {
            return [];
        }

        return resourceSet.Cast<DictionaryEntry>()
            .Where(static entry => entry.Key is string && entry.Value is string)
            .Select(entry => new LocalizedString(
                (string)entry.Key,
                (string)entry.Value!,
                resourceNotFound: false,
                searchedLocation: typeof(TResource).Assembly.FullName))
            .OrderBy(static value => value.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
