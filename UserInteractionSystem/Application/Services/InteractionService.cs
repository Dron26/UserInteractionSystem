using System.Globalization;
using System.Security.Claims;
using Microsoft.Data.Sqlite;
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
        var nowMoscow = InteractionTimeZone.ToMoscowTime(nowUtc);
        switch (filter.Period)
        {
            case InteractionDatePeriod.Today:
                var startOfMoscowDay = DateTime.SpecifyKind(nowMoscow.Date, DateTimeKind.Unspecified);
                fromUtc = InteractionTimeZone.ToUtc(startOfMoscowDay);
                toUtcExclusive = InteractionTimeZone.ToUtc(startOfMoscowDay.AddDays(1));
                break;
            case InteractionDatePeriod.Last7Days:
                fromUtc = InteractionTimeZone.ToUtc(nowMoscow.AddDays(-7));
                toUtcExclusive = nowUtc;
                break;
            case InteractionDatePeriod.LastMonth:
                fromUtc = InteractionTimeZone.ToUtc(nowMoscow.AddMonths(-1));
                toUtcExclusive = nowUtc;
                break;
            case InteractionDatePeriod.Custom:
                if (filter.FromDate.HasValue)
                {
                    var startOfCustomPeriod = DateTime.SpecifyKind(
                        filter.FromDate.Value.Date,
                        DateTimeKind.Unspecified);
                    fromUtc = InteractionTimeZone.ToUtc(startOfCustomPeriod);
                }

                if (filter.ToDate.HasValue)
                {
                    var endOfCustomPeriod = DateTime.SpecifyKind(
                        filter.ToDate.Value.Date.AddDays(1),
                        DateTimeKind.Unspecified);
                    toUtcExclusive = InteractionTimeZone.ToUtc(endOfCustomPeriod);
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
            .Where(user => user.IsActive && !user.IsArchived)
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
        var trimmedContent = content?.Trim();
        if (currentUser?.Identity?.IsAuthenticated != true ||
            !int.TryParse(
                currentUser.FindFirst("UserId")?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var userId) ||
            !Enum.IsDefined(channel) ||
            string.IsNullOrEmpty(trimmedContent) ||
            trimmedContent.Length > 5000 ||
            contactedAt is null)
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

        Interaction? interaction = null;
        try
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            var requestStatus = await _dbContext.Requests
                .Where(request => request.Id == requestId)
                .Select(request => (RequestStatus?)request.Status)
                .FirstOrDefaultAsync();

            if (requestStatus is null ||
                requestStatus is RequestStatus.Completed or RequestStatus.Cancelled)
            {
                return false;
            }

            interaction = new Interaction
            {
                RequestId = requestId,
                UserId = activeUser.Id,
                Channel = channel,
                Content = trimmedContent,
                CreatedAt = InteractionTimeZone.ToUtc(contactedAt.Value)
            };

            await _dbContext.Interactions.AddAsync(interaction);
            if (await _dbContext.SaveChangesAsync() == 0)
            {
                await transaction.RollbackAsync();
                _dbContext.Entry(interaction).State = EntityState.Detached;
                return false;
            }

            await transaction.CommitAsync();
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            if (interaction is not null)
            {
                _dbContext.Entry(interaction).State = EntityState.Detached;
            }

            return false;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqliteException sqliteException &&
            sqliteException.SqliteErrorCode is 5 or 6)
        {
            if (interaction is not null)
            {
                _dbContext.Entry(interaction).State = EntityState.Detached;
            }

            return false;
        }
    }
}
