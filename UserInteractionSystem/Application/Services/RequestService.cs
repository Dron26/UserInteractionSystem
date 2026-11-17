using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Infrastructure.Data;
using UserInteractionSystem.Application.Models;

namespace UserInteractionSystem.Application.Services;

public class RequestService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserService _userService;

    public RequestService(ApplicationDbContext dbContext, UserService userService)
    {
        _dbContext = dbContext;
        _userService = userService;
    }

    public async Task<List<Request>> GetRequestsAsync()
    {
        return await _dbContext.Requests
            .OrderByDescending(request => request.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> AddRequestAsync(Request request, ClaimsPrincipal? currentUser)
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

        if (!await _userService.IsAuthenticatedUserCurrentAsync(currentUser))
        {
            return false;
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

    public async Task<List<RequestListItem>> GetRequestListAsync()
    {
        return await _dbContext.Requests
            .Join(
                _dbContext.Clients,
                request => request.ClientId,
                client => client.Id,
                (request, client) => new RequestListItem
                {
                    Id = request.Id,
                    Title = request.Title,
                    ClientName = client.FullName,
                    Status = request.Status,
                    Priority = request.Priority,
                    CreatedAt = request.CreatedAt
                })
            .OrderByDescending(request => request.CreatedAt)
            .ToListAsync();
    }

    public async Task<RequestDashboardItem> GetRequestDashboardAsync()
    {
        var statistics = await _dbContext.Requests
            .GroupBy(_ => 1)
            .Select(group => new RequestDashboardItem
            {
                TotalCount = group.Count(),
                NewCount = group.Count(request => request.Status == RequestStatus.New),
                InProgressCount = group.Count(request => request.Status == RequestStatus.InProgress),
                WaitingForClientCount = group.Count(request => request.Status == RequestStatus.WaitingForClient),
                CompletedCount = group.Count(request => request.Status == RequestStatus.Completed)
            })
            .SingleOrDefaultAsync() ?? new RequestDashboardItem();

        statistics.RecentRequests = await _dbContext.Requests
            .Join(
                _dbContext.Clients,
                request => request.ClientId,
                client => client.Id,
                (request, client) => new RequestListItem
                {
                    Id = request.Id,
                    Title = request.Title,
                    ClientName = client.FullName,
                    Status = request.Status,
                    Priority = request.Priority,
                    CreatedAt = request.CreatedAt
                })
            .OrderByDescending(request => request.CreatedAt)
            .ThenByDescending(request => request.Id)
            .Take(5)
            .ToListAsync();

        return statistics;
    }

    public async Task<RequestDetailsItem?> GetRequestByIdAsync(int requestId)
    {
        return await _dbContext.Requests
            .Where(request => request.Id == requestId)
            .Join(
                _dbContext.Clients,
                request => request.ClientId,
                client => client.Id,
                (request, client) => new RequestDetailsItem
                {
                    Id = request.Id,
                    Title = request.Title,
                    Description = request.Description,
                    ClientName = client.FullName,
                    ResponsibleUserId = request.ResponsibleUserId,
                    ResponsibleUserName = _dbContext.Users
                        .Where(user => user.Id == request.ResponsibleUserId)
                        .Select(user => user.FullName)
                        .FirstOrDefault(),
                    Status = request.Status,
                    Priority = request.Priority,
                    CreatedAt = request.CreatedAt,
                    UpdatedAt = request.UpdatedAt
                })
            .FirstOrDefaultAsync();
    }

    public async Task<List<RequestAssigneeItem>> GetActiveUsersAsync()
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.IsActive)
            .OrderBy(user => user.FullName)
            .Select(user => new RequestAssigneeItem
            {
                Id = user.Id,
                FullName = user.FullName
            })
            .ToListAsync();
    }

    public async Task<bool> UpdateRequestAsync(
        int requestId,
        RequestStatus status,
        RequestPriority priority,
        int? responsibleUserId,
        ClaimsPrincipal? currentUser)
    {
        if (!Enum.IsDefined(status) ||
            priority is not (RequestPriority.Low or RequestPriority.Normal or RequestPriority.High))
        {
            return false;
        }

        var request = await _dbContext.Requests.FindAsync(requestId);

        if (request is null)
        {
            return false;
        }

        if (responsibleUserId.HasValue)
        {
            var responsibleUserCanBeKept = await _dbContext.Users
                .AnyAsync(user =>
                    user.Id == responsibleUserId.Value &&
                    (user.IsActive || request.ResponsibleUserId == responsibleUserId));

            if (!responsibleUserCanBeKept)
            {
                return false;
            }
        }

        if (!await _userService.IsAuthenticatedUserCurrentAsync(currentUser))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (status == RequestStatus.Completed && request.Status != RequestStatus.Completed)
        {
            request.CompletedAt = now;
        }
        else if (status != RequestStatus.Completed)
        {
            request.CompletedAt = null;
        }

        request.Status = status;
        request.Priority = priority;
        request.ResponsibleUserId = responsibleUserId;
        request.UpdatedAt = now;

        return await _dbContext.SaveChangesAsync() > 0;
    }
}
