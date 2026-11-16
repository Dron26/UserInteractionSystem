namespace UserInteractionSystem.Application.Models;

public class RequestDashboardItem
{
    public int TotalCount { get; set; }

    public int NewCount { get; set; }

    public int InProgressCount { get; set; }

    public int WaitingForClientCount { get; set; }

    public int CompletedCount { get; set; }

    public List<RequestListItem> RecentRequests { get; set; } = [];
}
