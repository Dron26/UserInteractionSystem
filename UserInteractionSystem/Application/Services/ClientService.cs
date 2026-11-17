using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Models;
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

    public async Task<List<Client>> GetClientsAsync(ClientListFilter? filter = null)
    {
        filter ??= new ClientListFilter();
        var query = _dbContext.Clients.AsNoTracking();
        query = filter.ArchiveState switch
        {
            ClientArchiveFilter.Active => query.Where(client => !client.IsArchived),
            ClientArchiveFilter.Archived => query.Where(client => client.IsArchived),
            ClientArchiveFilter.All => query,
            _ => query.Where(client => !client.IsArchived)
        };

        var fullName = filter.FullName?.Trim();
        var filterFullNameInMemory = !string.IsNullOrEmpty(fullName) && ContainsNonAscii(fullName);
        if (!string.IsNullOrEmpty(fullName) && !filterFullNameInMemory)
        {
            query = query.Where(client => EF.Functions.Like(
                EF.Functions.Collate(client.FullName, "NOCASE"),
                $"%{EscapeLikePattern(fullName)}%",
                "\\"));
        }

        var phone = NormalizePhone(filter.Phone);
        if (!string.IsNullOrEmpty(phone))
        {
            query = query.Where(client =>
                client.Phone != null &&
                EF.Functions.Like(
                    EF.Functions.Collate(
                        client.Phone.Replace(" ", string.Empty)
                            .Replace("(", string.Empty)
                            .Replace(")", string.Empty)
                            .Replace("-", string.Empty),
                        "NOCASE"),
                    $"%{EscapeLikePattern(phone)}%",
                    "\\"));
        }

        var email = filter.Email?.Trim();
        var filterEmailInMemory = !string.IsNullOrEmpty(email) && ContainsNonAscii(email);
        if (!string.IsNullOrEmpty(email) && !filterEmailInMemory)
        {
            query = query.Where(client => client.Email != null && EF.Functions.Like(
                EF.Functions.Collate(client.Email, "NOCASE"),
                $"%{EscapeLikePattern(email)}%",
                "\\"));
        }

        var comment = filter.Comment?.Trim();
        var filterCommentInMemory = !string.IsNullOrEmpty(comment) && ContainsNonAscii(comment);
        if (!string.IsNullOrEmpty(comment) && !filterCommentInMemory)
        {
            query = query.Where(client => client.Comment != null && EF.Functions.Like(
                EF.Functions.Collate(client.Comment, "NOCASE"),
                $"%{EscapeLikePattern(comment)}%",
                "\\"));
        }

        DateTime? fromUtc = null;
        DateTime? toUtcExclusive = null;
        var nowUtc = DateTime.UtcNow;
        switch (filter.Period)
        {
            case ClientDatePeriod.Today:
                var localToday = DateTime.Today;
                fromUtc = localToday.ToUniversalTime();
                toUtcExclusive = localToday.AddDays(1).ToUniversalTime();
                break;
            case ClientDatePeriod.LastWeek:
                fromUtc = nowUtc.AddDays(-7);
                toUtcExclusive = nowUtc;
                break;
            case ClientDatePeriod.LastMonth:
                fromUtc = nowUtc.AddMonths(-1);
                toUtcExclusive = nowUtc;
                break;
            case ClientDatePeriod.Custom:
                if (filter.FromDate.HasValue)
                {
                    fromUtc = DateTime.SpecifyKind(filter.FromDate.Value.Date, DateTimeKind.Local).ToUniversalTime();
                }

                if (filter.ToDate.HasValue)
                {
                    toUtcExclusive = DateTime.SpecifyKind(
                        filter.ToDate.Value.Date.AddDays(1),
                        DateTimeKind.Local).ToUniversalTime();
                }

                if (filter.FromDate.HasValue && filter.ToDate.HasValue &&
                    filter.FromDate.Value.Date > filter.ToDate.Value.Date)
                {
                    return [];
                }

                break;
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(client => client.CreatedAt >= fromUtc.Value);
        }

        if (toUtcExclusive.HasValue)
        {
            query = query.Where(client => client.CreatedAt < toUtcExclusive.Value);
        }

        var clients = await query
            .OrderBy(client => client.FullName)
            .ThenBy(client => client.Id)
            .ToListAsync();

        if (filterFullNameInMemory)
        {
            clients = clients.Where(client => client.FullName.Contains(fullName!, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (filterEmailInMemory)
        {
            clients = clients.Where(client => client.Email?.Contains(email!, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (filterCommentInMemory)
        {
            clients = clients.Where(client => client.Comment?.Contains(comment!, StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        return clients;
    }

    private static bool ContainsNonAscii(string value) => value.Any(character => character > 0x7F);

    private static string NormalizePhone(string? phone) => string.IsNullOrWhiteSpace(phone)
        ? string.Empty
        : phone.Replace(" ", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Replace("-", string.Empty)
            .Trim();

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

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
        if (!await _userService.IsAuthenticatedUserCurrentAsync(currentUser))
        {
            return false;
        }

        var client = await _dbContext.Clients.FindAsync(clientId);
        if (client is null || client.IsArchived)
        {
            return false;
        }

        client.IsArchived = true;
        await _dbContext.SaveChangesAsync();

        return true;
    }

    public async Task<bool> RestoreClientAsync(int clientId, ClaimsPrincipal? currentUser)
    {
        if (!await _userService.IsAuthenticatedUserCurrentAsync(currentUser))
        {
            return false;
        }

        var client = await _dbContext.Clients.FindAsync(clientId);
        if (client is null || !client.IsArchived)
        {
            return false;
        }

        client.IsArchived = false;
        await _dbContext.SaveChangesAsync();

        return true;
    }
}
