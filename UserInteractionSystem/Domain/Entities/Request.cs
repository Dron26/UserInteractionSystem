using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Domain.Entities;

public class Request
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public int? ResponsibleUserId { get; set; }

    public required string Title { get; set; }

    public required string Description { get; set; }

    public RequestStatus Status { get; set; }

    public RequestPriority Priority { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
