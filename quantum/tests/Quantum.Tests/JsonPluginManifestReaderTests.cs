using Quantum.Infrastructure.Plugins;

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
