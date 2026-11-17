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
