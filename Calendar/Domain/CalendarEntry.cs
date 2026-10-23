using NOF.Domain;

namespace Quantum.OfficialPlugins.Calendar.Domain;

public sealed class CalendarEntry
{
    private static readonly HashSet<string> SupportedStyles =
    [
        "violet",
        "blue",
        "coral",
        "green"
    ];

    private CalendarEntry()
    {
    }

    private CalendarEntry(
        Guid id,
        string title,
        string notes,
        CalendarEntryKind kind,
        DateOnly date,
        TimeOnly startTime,
        TimeOnly? endTime,
        string style,
        DateTimeOffset createdAt)
    {
        Id = id;
        CreatedAt = createdAt;
        Update(title, notes, kind, date, startTime, endTime, style, createdAt);
    }

    public Guid Id
    {
        get; private set;
    }

    public string Title { get; private set; } = string.Empty;
    public string Notes { get; private set; } = string.Empty;
    public CalendarEntryKind Kind
    {
        get; private set;
    }

    public DateOnly Date
    {
        get; private set;
    }

    public TimeOnly StartTime
    {
        get; private set;
    }

    public TimeOnly? EndTime
    {
        get; private set;
    }

    public bool IsCompleted
    {
        get; private set;
    }

    public string Style { get; private set; } = "violet";
    public DateTimeOffset CreatedAt
    {
        get; private set;
    }

    public DateTimeOffset UpdatedAt
    {
        get; private set;
    }

    public static CalendarEntry Create(
        string title,
        string notes,
        CalendarEntryKind kind,
        DateOnly date,
        TimeOnly startTime,
        TimeOnly? endTime,
        string style,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new CalendarEntry(
            Guid.NewGuid(), title, notes, kind, date, startTime, endTime, style,
            timeProvider.GetUtcNow());
    }

    public void Update(
        string title,
        string notes,
        CalendarEntryKind kind,
        DateOnly date,
        TimeOnly startTime,
        TimeOnly? endTime,
        string style,
        DateTimeOffset updatedAt)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("calendar_invalid_request", "Title is required.");
        }
        if (notes is null)
        {
            throw new DomainException("calendar_invalid_request", "Notes are required.");
        }
        if (string.IsNullOrWhiteSpace(style))
        {
            throw new DomainException("calendar_invalid_request", "Style is required.");
        }

        var normalizedTitle = title.Trim();
        var normalizedNotes = notes.Trim();
        var normalizedStyle = style.Trim();
        if (normalizedTitle.Length > 120)
        {
            throw new DomainException("calendar_invalid_request", "Titles cannot exceed 120 characters.");
        }

        if (normalizedNotes.Length > 1000)
        {
            throw new DomainException("calendar_invalid_request", "Notes cannot exceed 1,000 characters.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException("calendar_invalid_request", "Invalid entry type.");
        }

        if (!SupportedStyles.Contains(normalizedStyle))
        {
            throw new DomainException("calendar_invalid_request", "The selected color is not supported.");
        }

        if (kind == CalendarEntryKind.Event && endTime is not null && endTime <= startTime)
        {
            throw new DomainException("calendar_invalid_request", "End time must be later than start time.");
        }

        Title = normalizedTitle;
        Notes = normalizedNotes;
        Kind = kind;
        Date = date;
        StartTime = startTime;
        EndTime = kind == CalendarEntryKind.Event ? endTime : null;
        Style = normalizedStyle;
        UpdatedAt = updatedAt;
    }

    public void SetCompleted(bool isCompleted, DateTimeOffset updatedAt)
    {
        IsCompleted = isCompleted;
        UpdatedAt = updatedAt;
    }
}
