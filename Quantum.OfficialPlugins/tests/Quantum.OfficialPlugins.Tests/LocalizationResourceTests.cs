using System.Xml.Linq;
using Xunit;

namespace Quantum.Tests;

public sealed class LocalizationResourceTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("calendar")]
    [InlineData("ledger")]
    public void EnglishNeutralResourceKeysHaveCompleteChineseTranslations(string plugin)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "localization", plugin);
        var baseline = Read(Path.Combine(directory, "PluginStrings.resx"));
        var chinese = Read(Path.Combine(directory, "PluginStrings.zh-CN.resx"));

        Assert.NotEmpty(baseline);
        Assert.DoesNotContain(baseline.Keys, ContainsCjkCharacter);
        Assert.Equal(
            baseline.Count,
            baseline.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            baseline.Keys.Order(StringComparer.Ordinal),
            chinese.Keys.Order(StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> Read(string path)
        => XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element => element.Attribute("name")!.Value,
            element => element.Element("value")!.Value,
            StringComparer.Ordinal);

    private static bool ContainsCjkCharacter(string value)
        => value.Any(character => character is >= '\u4e00' and <= '\u9fff');
}
