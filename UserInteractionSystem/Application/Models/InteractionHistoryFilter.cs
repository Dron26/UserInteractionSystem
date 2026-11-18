using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Application.Models;

public enum InteractionDatePeriod
{
    AllTime,
    Today,
    Last7Days,
    LastMonth,
    Custom
}

public class InteractionHistoryFilter
{
    public int? UserId { get; set; }

    public InteractionChannel? Channel { get; set; }

    public InteractionDatePeriod Period { get; set; } = InteractionDatePeriod.AllTime;

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }
}
