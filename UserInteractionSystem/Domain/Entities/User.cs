using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Domain.Entities;

public class User
{
    public int Id { get; set; }

    public required string UserName { get; set; }

    public required string PasswordHash { get; set; }

    public required string FullName { get; set; }

    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
