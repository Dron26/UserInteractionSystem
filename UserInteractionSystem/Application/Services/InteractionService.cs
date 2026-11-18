using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Models;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Infrastructure.Data;

namespace UserInteractionSystem.Application.Services;

public class InteractionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserService _userService;

    public InteractionService(ApplicationDbContext dbContext, UserService userService)
    {
        _dbContext = dbContext;
        _userService = userService;
    }

    public async Task<List<InteractionHistoryItem>> GetInteractionsByRequestIdAsync(
        int requestId,
        InteractionHistoryFilter? filter = null)
    {
        filter ??= new InteractionHistoryFilter();
        var query = _dbContext.Interactions
            .AsNoTracking()
            .Where(interaction => interaction.RequestId == requestId);

        if (filter.UserId.HasValue)
        {
            query = query.Where(interaction => interaction.UserId == filter.UserId.Value);
        }

        if (filter.Channel.HasValue)
        {
            query = query.Where(interaction => interaction.Channel == filter.Channel.Value);
        }

        DateTime? fromUtc = null;
        DateTime? toUtcExclusive = null;
        var nowUtc = DateTime.UtcNow;
        switch (filter.Period)
        {
            case InteractionDatePeriod.Today:
                var localToday = DateTime.Today;
                fromUtc = localToday.ToUniversalTime();
                toUtcExclusive = localToday.AddDays(1).ToUniversalTime();
                break;
            case InteractionDatePeriod.Last7Days:
                fromUtc = nowUtc.AddDays(-7);
                toUtcExclusive = nowUtc;
                break;
            case InteractionDatePeriod.LastMonth:
                fromUtc = nowUtc.AddMonths(-1);
                toUtcExclusive = nowUtc;
                break;
            case InteractionDatePeriod.Custom:
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
            query = query.Where(interaction => interaction.CreatedAt >= fromUtc.Value);
        }

        if (toUtcExclusive.HasValue)
        {
            query = query.Where(interaction => interaction.CreatedAt < toUtcExclusive.Value);
        }

        return await query
            .Join(
                _dbContext.Users,
                interaction => interaction.UserId,
                user => user.Id,
                (interaction, user) => new InteractionHistoryItem
                {
                    Id = interaction.Id,
                    Channel = interaction.Channel,
                    Content = interaction.Content,
                    CreatedAt = interaction.CreatedAt,
                    UserName = user.FullName
                })
            .OrderByDescending(interaction => interaction.CreatedAt)
            .ThenByDescending(interaction => interaction.Id)
            .ToListAsync();
    }

    public async Task<List<InteractionUserItem>> GetActiveUsersAsync()
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.IsActive)
            .OrderBy(user => user.FullName)
            .Select(user => new InteractionUserItem
            {
                Id = user.Id,
                FullName = user.FullName
            })
            .ToListAsync();
    }

    public async Task<List<InteractionUserItem>> GetInteractionAuthorsByRequestIdAsync(int requestId)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(user => _dbContext.Interactions.Any(interaction =>
                interaction.RequestId == requestId && interaction.UserId == user.Id))
            .OrderBy(user => user.FullName)
            .Select(user => new InteractionUserItem
            {
                Id = user.Id,
                FullName = user.FullName
            })
            .ToListAsync();
    }

    public async Task<bool> AddInteractionAsync(
        int requestId,
        ClaimsPrincipal? currentUser,
        InteractionChannel channel,
        string? content,
        DateTime? contactedAt)
    {
        if (currentUser?.Identity?.IsAuthenticated != true ||
            !int.TryParse(
                currentUser.FindFirst("UserId")?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var userId) ||
            !Enum.IsDefined(channel) ||
            string.IsNullOrWhiteSpace(content) ||
            contactedAt is null)
        {
            return false;
        }

        var requestExists = await _dbContext.Requests
            .AnyAsync(request => request.Id == requestId);

        if (!requestExists)
        {
            return false;
        }

        var activeUser = await _dbContext.Users
            .FirstOrDefaultAsync(user => user.Id == userId && user.IsActive);

        if (activeUser is null ||
            !await _userService.IsAuthenticatedUserCurrentAsync(currentUser))
        {
            return false;
        }

        var interaction = new Interaction
        {
            RequestId = requestId,
            UserId = activeUser.Id,
            Channel = channel,
            Content = content.Trim(),
            CreatedAt = contactedAt.Value.ToUniversalTime()
        };

        await _dbContext.Interactions.AddAsync(interaction);
        return await _dbContext.SaveChangesAsync() > 0;
    }
}
