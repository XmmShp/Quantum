namespace Quantum.OfficialPlugins.Calendar.Application;

/// <summary>Presentation port for rebuilding the plugin's published mobile page.</summary>
public interface ICalendarMobilePages
{
    Task PublishAsync(CancellationToken cancellationToken = default);
}
