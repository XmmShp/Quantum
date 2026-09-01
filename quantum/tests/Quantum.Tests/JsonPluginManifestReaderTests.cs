using Quantum.Plugins;

namespace Quantum.Tests;

public sealed class JsonPluginManifestReaderTests
{
    [Fact]
    public void Read_MapsManifestAndUiContributions()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "plugin.json"),
            """
            {
              "id": "quantum.plugin.test",
              "version": "1.2.0-beta.1",
              "entryAssembly": "Test.dll",
              "dependencies": [{ "id": "core", "minVersion": "1.0.0" }],
              "integrations": [{ "id": "optional-addon", "minVersion": "2.0.0" }],
              "permissions": [{ "name": "files.read", "required": true }],
              "ui": {
                "routes": [{
                  "path": "/plugins/test",
                  "component": "Test.Pages.Index",
                  "title": "Test"
                }]
              },
              "web": {
                "head": ["<link rel=\"stylesheet\" href=\"site.css\">"],
                "postBlazor": []
              }
            }
            """);
        File.WriteAllBytes(Path.Combine(directory.Path, "Test.dll"), []);

        var candidate = new JsonPluginManifestReader().Read(directory.Path);

        Assert.Equal("quantum.plugin.test", candidate.Manifest.Id.Value);
        Assert.Equal("1.2.0-beta.1", candidate.Manifest.Version.ToString());
        Assert.Single(candidate.Manifest.Dependencies);
        var integration = Assert.Single(candidate.Manifest.Integrations);
        Assert.Equal("optional-addon", integration.Id.Value);
        Assert.Equal("2.0.0", integration.MinimumVersion.ToString());
        Assert.Single(candidate.Manifest.Permissions);
        Assert.Single(candidate.Manifest.Routes);
        Assert.Single(candidate.Manifest.Web.Head);
    }

    [Fact]
    public void Read_RejectsUnknownManifestFields()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "plugin.json"),
            """
            {
              "id": "quantum.plugin.test",
              "version": "1.0.0",
              "entryAssembly": "Test.dll",
              "unexpected": true
            }
            """);
        File.WriteAllBytes(Path.Combine(directory.Path, "Test.dll"), []);

        Assert.ThrowsAny<Exception>(() => new JsonPluginManifestReader().Read(directory.Path));
    }

    [Fact]
    public void Read_MapsWebRuntimeAndViewWithoutAssembly()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "wwwroot", "dist"));
        File.WriteAllText(Path.Combine(directory.Path, "wwwroot", "dist", "plugin.js"), "export default {};");
        File.WriteAllText(
            Path.Combine(directory.Path, "plugin.json"),
            """
            {
              "id": "quantum.plugin.web",
              "version": "1.0.0",
              "runtime": {
                "kind": "web",
                "entry": "dist/plugin.js"
              },
              "ui": {
                "routes": [{
                  "path": "/plugins/web",
                  "view": "main",
                  "title": "Web plugin"
                }]
              }
            }
            """);

        var candidate = new JsonPluginManifestReader().Read(directory.Path);

        Assert.Equal(PluginRuntimeKind.Web, candidate.Manifest.Runtime.Kind);
        Assert.Equal("dist/plugin.js", candidate.Manifest.Runtime.Entry);
        Assert.Null(candidate.Manifest.EntryAssembly);
        var route = Assert.Single(candidate.Manifest.Routes);
        Assert.Equal("main", route.View);
        Assert.Null(route.Component);
    }

    [Fact]
    public void Read_RejectsManifestWithLegacyAndDiscriminatedRuntime()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "plugin.json"),
            """
            {
              "id": "quantum.plugin.test",
              "version": "1.0.0",
              "entryAssembly": "Test.dll",
              "runtime": { "kind": "web", "entry": "plugin.js" }
            }
            """);

        Assert.Throws<InvalidDataException>(() => new JsonPluginManifestReader().Read(directory.Path));
    }

    [Fact]
    public void Read_RejectsPlatformSpecificWebEntryPath()
    {
        using var directory = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(directory.Path, "plugin.json"),
            """
            {
              "id": "quantum.plugin.web",
              "version": "1.0.0",
              "runtime": { "kind": "web", "entry": "C:/plugin.js" }
            }
            """);

        Assert.Throws<ArgumentException>(() => new JsonPluginManifestReader().Read(directory.Path));
    }

    [Fact]
    public void Read_RejectsWebRouteWithoutView()
    {
        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(directory.Path, "wwwroot"));
        File.WriteAllText(Path.Combine(directory.Path, "wwwroot", "plugin.js"), "export default {};");
        File.WriteAllText(
            Path.Combine(directory.Path, "plugin.json"),
            """
            {
              "id": "quantum.plugin.web",
              "version": "1.0.0",
              "runtime": { "kind": "web", "entry": "plugin.js" },
              "ui": { "routes": [{ "path": "/plugins/web" }] }
            }
            """);

        Assert.Throws<ArgumentException>(() => new JsonPluginManifestReader().Read(directory.Path));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"quantum-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
