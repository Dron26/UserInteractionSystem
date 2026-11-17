using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Application.Models;

public class AuthenticatedUserItem
{
    public int Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public bool MustChangePassword { get; set; }
}
