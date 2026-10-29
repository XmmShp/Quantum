using System.ComponentModel;

namespace Quantum.OfficialPlugins.Calendar.Application;

[Description("Rebuild the mobile page from current desktop state. Does not start external processes or model inference.")]
public sealed record RefreshCalendarMobilePageRequest;
