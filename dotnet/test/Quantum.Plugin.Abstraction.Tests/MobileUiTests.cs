using System.Text.Json;
using System.Text.Json.Serialization;
using MobileUi = Quantum.Plugin.Abstraction.Mobile;
using Xunit;

namespace Quantum.Plugin.Abstraction.Tests;

public sealed class MobileUiTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    [Fact]
    public void AttachmentsRequireVersionTwoAndBindAsAnInput()
    {
        var page = MobileUi.Page("chat", "Chat", MobileUi.VStack("root", MobileUi.Attachments("files"),
            MobileUi.Button("send", "Send").OnClick(MobileUi.Rpc("plugin.chat.send").Input("attachments", "files"))));
        Assert.Equal(2, page.SchemaVersion);
        Assert.Equal("Attachments", MobileUiValidator.Validate(page)["files"].Type);
        Assert.Throws<ArgumentException>(() => MobileUiValidator.Validate(page with { SchemaVersion = 1 }));
    }
    [Fact]
    public void SharedVersionOneFixtureRoundTripsWithoutChangingBindings()
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mobile-page-v1.json")));
        var page = json.RootElement.Deserialize<MobilePage>(Options)!;
        var nodes = MobileUiValidator.Validate(page);
        Assert.Equal("content", nodes["save"].Action!.Inputs["text"]);
        Assert.Equal("pinned", nodes["save"].Action!.Inputs["pinned"]);
        var restored = JsonSerializer.Deserialize<MobilePage>(JsonSerializer.Serialize(page, Options), Options)!;
        Assert.Equal(nodes.Count, MobileUiValidator.Validate(restored).Count);
        Assert.Throws<ArgumentException>(() => MobileUiValidator.Validate(page with { SchemaVersion = 99 }));
    }

    [Fact]
    public void DeclaredBindingsMustReferenceInputsAndCannotOverwriteConstants()
    {
        var action = MobileUi.Rpc("quantum.plugin.notes.notes.create").Constant("text", "fixed").Input("text", "input");
        var page = MobileUi.Page("notes", "Notes", MobileUi.VStack("root", MobileUi.TextField("input"), MobileUi.Button("save", "Save").OnClick(action)));
        Assert.Throws<ArgumentException>(() => MobileUiValidator.Validate(page));
    }
    [Fact]
    public void DeclarationsRejectTransportFieldsUnknownFieldsAndDuplicateNodes()
    {
        Assert.Null(typeof(MobileNode).GetProperty("ActionId"));
        Assert.Null(typeof(MobileNode).GetProperty("InputIds"));
        var invalid = MobileUi.Page("notes", "Notes", MobileUi.VStack("root", MobileUi.Text("a", "one"), MobileUi.Text("a", "two")));
        Assert.Throws<ArgumentException>(() => MobileUiValidator.Validate(invalid));
        Assert.Throws<System.Text.Json.JsonException>(() => JsonSerializer.Deserialize<MobileNode>("""{"type":"Text","id":"a","script":"evil"}""", Options));
    }
    [Theory]
    [InlineData("""{"type":"Text"}""")]
    [InlineData("""{"id":"text"}""")]
    [InlineData("""{"type":"Text","id":"text","actionId":"private"}""")]
    [InlineData("""{"type":"Text","id":"text","inputIds":[]}""")]
    public void RequiredDeclarationMembersAndInternalTransportBoundaryAreEnforced(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MobileNode>(json, Options));
    }

    [Fact]
    public void RpcBuilderIsImmutableAndSdkHasNoTransportDependency()
    {
        var original = MobileUi.Rpc("plugin.notes.create");
        var derived = original.Constant("fixed", 3);
        Assert.Empty(original.Constants);
        Assert.Equal(3, derived.Constants["fixed"].GetInt32());
        Assert.Throws<ArgumentException>(() => original.Constant("privateDto", new { text = "plugin object" }));
        Assert.DoesNotContain(typeof(MobilePage).Assembly.GetExportedTypes(), t => t.Name.StartsWith("Bridge") || t.Name == "MobileJson");
        Assert.DoesNotContain(typeof(MobilePage).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Quantum.Mobile."));
    }
}
