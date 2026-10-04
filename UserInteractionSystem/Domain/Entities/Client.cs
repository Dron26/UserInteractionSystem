using System.ComponentModel.DataAnnotations;

namespace UserInteractionSystem.Domain.Entities;

public class Client
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Введите ФИО клиента")]
    public required string FullName { get; set; }

    public string? Phone { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    public string? Comment { get; set; }

    public bool IsArchived { get; set; } = false;

    public DateTime CreatedAt { get; set; }
}
