using Microsoft.Extensions.DependencyInjection;
using NOF.Domain;
using Quantum.ExampleCalendarPlugin.Application;
using Quantum.ExampleCalendarPlugin.Domain;
using Quantum.Plugin.Abstraction;

namespace Quantum.ExampleCalendarPlugin;

public sealed class CalendarPlugin : IQuantumPlugin
{
    public static async Task StartAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var calendarItems = services.GetRequiredService<IRepository<CalendarItem>>();
        if (calendarItems.AsNoTracking().Any())
        {
            return;
        }

        var applicationService = services.GetRequiredService<ICalendarItemApplicationService>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var month = new DateOnly(today.Year, today.Month, 1);
        foreach (var seed in CreateSeedItems(month))
        {
            await applicationService.CreateAsync(seed, cancellationToken);
        }
    }

    public static Task StopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static IEnumerable<CreateCalendarItemRequest> CreateSeedItems(DateOnly month)
    {
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        yield return CreateSeed(3, "产品同步", 9, 30, "和团队对齐本月目标与关键交付。", "event-violet");
        yield return CreateSeed(7, "专注时间", 14, 0, "关闭通知，为核心工作留出两个小时。", "event-blue");
        yield return CreateSeed(12, "设计评审", 10, 30, "检查交互细节并确认下一轮迭代范围。", "event-coral");
        yield return CreateSeed(18, "读书小组", 19, 0, "带上本周的笔记和一个值得讨论的问题。", "event-green");
        yield return CreateSeed(23, "版本发布", 16, 0, "完成检查清单，发布后观察关键指标。", "event-violet");
        yield return CreateSeed(27, "月度回顾", 17, 30, "记录进展、遗留问题和下个月的第一步。", "event-blue");

        CreateCalendarItemRequest CreateSeed(
            int day,
            string title,
            int hour,
            int minute,
            string description,
            string style)
            => new(
                title,
                description,
                new DateOnly(month.Year, month.Month, Math.Min(day, daysInMonth)),
                new TimeOnly(hour, minute),
                style);
    }
}
