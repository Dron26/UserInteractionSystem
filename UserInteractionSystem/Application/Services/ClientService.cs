using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Infrastructure.Data;

namespace UserInteractionSystem.Application.Services;

public class ClientService
{
    private readonly ApplicationDbContext _dbContext;

    public ClientService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Client>> GetClientsAsync()
    {
        return await _dbContext.Clients
            .Where(client => !client.IsArchived)
            .ToListAsync();
    }

    public async Task<Client?> GetClientByIdAsync(int clientId)
    {
        return await _dbContext.Clients.FindAsync(clientId);
    }

    public async Task<bool> AddClientAsync(Client client)
    {
        await _dbContext.Clients.AddAsync(client);
        
        var result = await _dbContext.SaveChangesAsync();

        return result > 0;
    }

    public async Task<bool> ArchiveClientAsync(int clientId)
    {
        var client = await _dbContext.Clients.FindAsync(clientId);

        if (client is null)
        {
            return false;
        }

        client.IsArchived = true;
        await _dbContext.SaveChangesAsync();

        return true;
    }
}
