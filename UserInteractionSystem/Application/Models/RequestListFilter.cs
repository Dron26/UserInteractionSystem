using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Application.Models;

public enum RequestDatePeriod
{
    AllTime,
    Today,
    LastWeek,
    LastMonth,
    Custom
}

public class RequestListFilter
{
    public int? ResponsibleUserId { get; set; }

    public RequestStatus? Status { get; set; }

    public RequestPriority? Priority { get; set; }

    public RequestDatePeriod Period { get; set; } = RequestDatePeriod.AllTime;

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }
}
