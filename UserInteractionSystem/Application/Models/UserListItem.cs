using System.ComponentModel.DataAnnotations;
using UserInteractionSystem.Domain.Enums;

namespace UserInteractionSystem.Application.Models;

public class UserListItem
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Укажите ФИО сотрудника.")]
    [StringLength(200, ErrorMessage = "ФИО не должно превышать 200 символов.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Укажите логин.")]
    [StringLength(100, ErrorMessage = "Логин не должен превышать 100 символов.")]
    public string UserName { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
}
