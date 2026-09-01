namespace Quantum.ExampleCalendarPlugin.Domain;

public sealed class CalendarItem
{
    private static readonly HashSet<string> SupportedStyles =
    [
        "event-violet",
        "event-blue",
        "event-coral",
        "event-green"
    ];

    private CalendarItem()
    {
    }

    private CalendarItem(
        Guid id,
        string title,
        string description,
        DateOnly date,
        TimeOnly startTime,
        string style,
        DateTimeOffset createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
        Update(title, description, date, startTime, style, createdAt);
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public string Style { get; private set; } = "event-violet";

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CalendarItem Create(
        string title,
        string description,
        DateOnly date,
        TimeOnly startTime,
        string style,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new CalendarItem(
            Guid.NewGuid(),
            title,
            description,
            date,
            startTime,
            style,
            timeProvider.GetUtcNow());
    }

    public void Update(
        string title,
        string description,
        DateOnly date,
        TimeOnly startTime,
        string style,
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(style);

        var normalizedTitle = title.Trim();
        var normalizedDescription = description.Trim();
        var normalizedStyle = style.Trim();
        if (normalizedTitle.Length > 120)
        {
            throw new ArgumentException("事项标题不能超过 120 个字符。", nameof(title));
        }

        if (normalizedDescription.Length > 1000)
        {
            throw new ArgumentException("事项说明不能超过 1000 个字符。", nameof(description));
        }

        if (!SupportedStyles.Contains(normalizedStyle))
        {
            throw new ArgumentException("事项颜色不受支持。", nameof(style));
        }

        Title = normalizedTitle;
        Description = normalizedDescription;
        Date = date;
        StartTime = startTime;
        Style = normalizedStyle;
        UpdatedAt = updatedAt;
    }
}
