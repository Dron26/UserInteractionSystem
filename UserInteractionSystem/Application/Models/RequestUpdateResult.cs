namespace UserInteractionSystem.Application.Models;

public enum RequestUpdateResult
{
    Success,
    InvalidData,
    NotFound,
    Conflict,
    Unauthorized,
    TemporaryFailure
}
