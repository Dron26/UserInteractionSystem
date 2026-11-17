using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Infrastructure.Data;

namespace UserInteractionSystem.Application.Services;

public class ClientService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserService _userService;

    public ClientService(ApplicationDbContext dbContext, UserService userService)
    {
        _dbContext = dbContext;
        _userService = userService;
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

    public async Task<bool> UpdateClientAsync(Client client, ClaimsPrincipal? currentUser)
    {
        var existingClient = await _dbContext.Clients.FindAsync(client.Id);

        if (existingClient is null || existingClient.IsArchived ||
            !await _userService.IsAuthenticatedUserCurrentAsync(currentUser))
        {
            return false;
        }

        existingClient.FullName = client.FullName;
        existingClient.Phone = client.Phone;
        existingClient.Email = client.Email;
        existingClient.Comment = client.Comment;

        await _dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<bool> AddClientAsync(Client client, ClaimsPrincipal? currentUser)
    {
        if (!await _userService.IsAuthenticatedUserCurrentAsync(currentUser))
        {
            return false;
        }

        await _dbContext.Clients.AddAsync(client);
        
        var result = await _dbContext.SaveChangesAsync();

        return result > 0;
    }

    public async Task<bool> ArchiveClientAsync(int clientId, ClaimsPrincipal? currentUser)
    {
        var client = await _dbContext.Clients.FindAsync(clientId);

        if (client is null || !await _userService.IsAuthenticatedUserCurrentAsync(currentUser))
        {
            return false;
        }

        client.IsArchived = true;
        await _dbContext.SaveChangesAsync();

        return true;
    }
}
