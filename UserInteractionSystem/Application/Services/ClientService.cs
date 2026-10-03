using UserInteractionSystem.Infrastructure.Data;

namespace UserInteractionSystem.Application.Services;

public class ClientService
{
    private readonly ApplicationDbContext _dbContext;

    public ClientService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }
}
