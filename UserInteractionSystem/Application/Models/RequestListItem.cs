using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Application.Models;

public class RequestListItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public RequestStatus Status { get; set; }

    public RequestPriority Priority { get; set; }

    public DateTime CreatedAt { get; set; }
}