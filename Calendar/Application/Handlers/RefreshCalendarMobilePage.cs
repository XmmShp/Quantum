using NOF.Contract;

namespace Quantum.OfficialPlugins.Calendar.Application;

public sealed class RefreshCalendarMobilePage(ICalendarMobilePages pages) : CalendarRpcService.RefreshMobilePage
{
    public override async Task<Result> HandleAsync(RefreshCalendarMobilePageRequest request, Context context, CancellationToken cancellationToken)
    {
        await pages.PublishAsync(cancellationToken);
        return Result.Success();
    }
}
