namespace Quantum.OfficialPlugins.Codex.Application;

/// <summary>Presentation port for rebuilding the plugin's published mobile page.</summary>
public interface ICodexMobilePages
{
    Task PublishAsync(CancellationToken cancellationToken = default);
}
