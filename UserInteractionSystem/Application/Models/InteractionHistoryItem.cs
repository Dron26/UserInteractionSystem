using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Application.Models;

public class InteractionHistoryItem
{
    public int Id { get; set; }

    public InteractionChannel Channel { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string UserName { get; set; } = string.Empty;
}
