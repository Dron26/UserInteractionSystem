using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Domain.Entities;

public class Interaction
{
    public int Id { get; set; }

    public int RequestId { get; set; }

    public int UserId { get; set; }

    public InteractionChannel Channel { get; set; }

    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; }
}
