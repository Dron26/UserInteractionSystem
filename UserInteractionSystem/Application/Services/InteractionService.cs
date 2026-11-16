using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Models;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Infrastructure.Data;

namespace UserInteractionSystem.Application.Services;

public class InteractionService
{
    private readonly ApplicationDbContext _dbContext;

    public InteractionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<InteractionHistoryItem>> GetInteractionsByRequestIdAsync(int requestId)
    {
        return await _dbContext.Interactions
            .AsNoTracking()
            .Where(interaction => interaction.RequestId == requestId)
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
            .OrderBy(interaction => interaction.CreatedAt)
            .ThenBy(interaction => interaction.Id)
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

    public async Task<bool> AddInteractionAsync(
        int requestId,
        int? userId,
        InteractionChannel channel,
        string? content,
        DateTime? contactedAt)
    {
        if (userId is null ||
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

        var userIsActive = await _dbContext.Users
            .AnyAsync(user => user.Id == userId.Value && user.IsActive);

        if (!userIsActive)
        {
            return false;
        }

        var interaction = new Interaction
        {
            RequestId = requestId,
            UserId = userId.Value,
            Channel = channel,
            Content = content.Trim(),
            CreatedAt = contactedAt.Value.ToUniversalTime()
        };

        await _dbContext.Interactions.AddAsync(interaction);
        return await _dbContext.SaveChangesAsync() > 0;
    }
}
