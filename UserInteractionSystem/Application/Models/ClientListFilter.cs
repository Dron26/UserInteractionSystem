namespace UserInteractionSystem.Application.Models;

public enum ClientDatePeriod
{
    AllTime,
    Today,
    LastWeek,
    LastMonth,
    Custom
}

public enum ClientArchiveFilter
{
    Active,
    Archived,
    All
}

public class ClientListFilter
{
    public string? FullName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Comment { get; set; }

    public ClientDatePeriod Period { get; set; } = ClientDatePeriod.AllTime;

    public ClientArchiveFilter ArchiveState { get; set; } = ClientArchiveFilter.Active;

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }
}
