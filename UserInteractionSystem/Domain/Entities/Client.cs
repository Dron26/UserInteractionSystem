namespace UserInteractionSystem.Domain.Entities;

public class Client
{
    public int Id { get; set; }

    public required string FullName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Comment { get; set; }

    public bool IsArchived { get; set; } = false;

    public DateTime CreatedAt { get; set; }
}
