using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Infrastructure.Data;

namespace UserInteractionSystem.Application.Services;

public class RequestService
{
    private readonly ApplicationDbContext _dbContext;

    public RequestService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Request>> GetRequestsAsync()
    {
        return await _dbContext.Requests
            .OrderByDescending(request => request.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> AddRequestAsync(Request request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.Description))
        {
            return false;
        }

        var clientExists = await _dbContext.Clients
            .AnyAsync(client => client.Id == request.ClientId && !client.IsArchived);

        if (!clientExists)
        {
            return false;
        }

        if (request.ResponsibleUserId.HasValue)
        {
            var responsibleUserIsActive = await _dbContext.Users
                .AnyAsync(user => user.Id == request.ResponsibleUserId.Value && user.IsActive);

            if (!responsibleUserIsActive)
            {
                return false;
            }
        }

        var now = DateTime.UtcNow;
        request.Title = request.Title.Trim();
        request.Description = request.Description.Trim();
        request.Status = RequestStatus.New;
        request.Priority = RequestPriority.Normal;
        request.CreatedAt = now;
        request.UpdatedAt = now;
        request.CompletedAt = null;

        await _dbContext.Requests.AddAsync(request);
        var result = await _dbContext.SaveChangesAsync();

        return result > 0;
    }
}
