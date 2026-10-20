using Quantum.Mobile.Protocol;
using MobileUi = Quantum.Mobile.Protocol.Mobile;
using Xunit;

namespace Quantum.Plugin.Abstraction.Tests;

public sealed class MobileUiTests
{
    [Fact]
    public void SharedVersionOneFixtureRoundTripsWithoutChangingBindings()
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mobile-page-v1.json")));
        var page = MobileJson.Read<MobilePage>(json.RootElement);
        var nodes = MobileUiValidator.Validate(page);
        Assert.Equal("content", nodes["save"].Action!.Inputs["text"]);
        Assert.Equal("pinned", nodes["save"].Action!.Inputs["pinned"]);
        var restored = MobileJson.Read<MobilePage>(MobileJson.Element(page));
        Assert.Equal(nodes.Count, MobileUiValidator.Validate(restored).Count);
        Assert.Throws<ArgumentException>(() => MobileUiValidator.Validate(page with { SchemaVersion = 2 }));
    }

    [Fact]
    public void DeclaredBindingsMustReferenceInputsAndCannotOverwriteConstants()
    {
        var action = MobileUi.Rpc("quantum.plugin.notes.notes.create").Constant("text", "fixed").Input("text", "input");
        var page = MobileUi.Page("notes", "Notes", MobileUi.VStack("root", MobileUi.TextField("input"), MobileUi.Button("save", "Save").OnClick(action)));
        Assert.Throws<ArgumentException>(() => MobileUiValidator.Validate(page));
    }
    [Fact]
    public void MobileWireRejectsExecutableActionsUnknownFieldsAndDuplicateNodes()
    {
        var page = MobileUi.Page("notes", "Notes", MobileUi.Button("save", "Save").OnClick(MobileUi.Rpc("plugin.notes.create")));
        Assert.Throws<ArgumentException>(() => MobileUiValidator.Validate(page, wire: true));
        var invalid = MobileUi.Page("notes", "Notes", MobileUi.VStack("root", MobileUi.Text("a", "one"), MobileUi.Text("a", "two")));
        Assert.Throws<ArgumentException>(() => MobileUiValidator.Validate(invalid));
        Assert.Throws<System.Text.Json.JsonException>(() => MobileJson.Read<MobileNode>(MobileJson.Element(new { type = "Text", id = "a", script = "evil" })));
    }
    [Fact]
    public void RpcBuilderIsImmutableAndProtocolHasNoPluginDependency()
    {
        var original = MobileUi.Rpc("plugin.notes.create");
        var derived = original.Constant("fixed", 3);
        Assert.Empty(original.Constants);
        Assert.Equal(3, derived.Constants["fixed"].GetInt32());
        Assert.Throws<ArgumentException>(() => original.Constant("privateDto", new { text = "plugin object" }));
        Assert.DoesNotContain(typeof(MobilePage).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("NOF") || a.Name.Contains("Plugin"));
    }
}
