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
            string.IsNullOrWhiteSpace(request.Description) ||
            request.Description.Trim().Length > 5000)
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
                .AnyAsync(user => user.Id == request.ResponsibleUserId.Value && user.IsActive && !user.IsArchived);

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

    public async Task<List<RequestListItem>> GetRequestListAsync(RequestListFilter? filter = null)
    {
        filter ??= new RequestListFilter();

        var query = _dbContext.Requests
            .AsNoTracking()
            .AsQueryable();

        if (filter.ResponsibleUserId.HasValue)
        {
            query = query.Where(request => request.ResponsibleUserId == filter.ResponsibleUserId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(request => request.Status == filter.Status.Value);
        }

        if (filter.Priority.HasValue)
        {
            query = query.Where(request => request.Priority == filter.Priority.Value);
        }

        DateTime? fromUtc = null;
        DateTime? toUtcExclusive = null;
        var nowUtc = DateTime.UtcNow;
        switch (filter.Period)
        {
            case RequestDatePeriod.Today:
                var localToday = DateTime.Today;
                fromUtc = localToday.ToUniversalTime();
                toUtcExclusive = localToday.AddDays(1).ToUniversalTime();
                break;
            case RequestDatePeriod.LastWeek:
                fromUtc = nowUtc.AddDays(-7);
                toUtcExclusive = nowUtc;
                break;
            case RequestDatePeriod.LastMonth:
                fromUtc = nowUtc.AddMonths(-1);
                toUtcExclusive = nowUtc;
                break;
            case RequestDatePeriod.Custom:
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
            query = query.Where(request => request.CreatedAt >= fromUtc.Value);
        }

        if (toUtcExclusive.HasValue)
        {
            query = query.Where(request => request.CreatedAt < toUtcExclusive.Value);
        }

        return await query
            .Join(
                _dbContext.Clients,
                request => request.ClientId,
                client => client.Id,
                (request, client) => new RequestListItem
                {
                    Id = request.Id,
                    Title = request.Title,
                    ClientName = client.FullName,
                    ResponsibleUserId = request.ResponsibleUserId,
                    ResponsibleUserName = _dbContext.Users
                        .Where(user => user.Id == request.ResponsibleUserId)
                        .Select(user => user.FullName)
                        .FirstOrDefault(),
                    Status = request.Status,
                    Priority = request.Priority,
                    CreatedAt = request.CreatedAt
                })
            .OrderByDescending(request => request.CreatedAt)
            .ThenByDescending(request => request.Id)
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
            .Where(user => user.IsActive && !user.IsArchived)
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

        if (request is null ||
            (request.Status != status && !IsAllowedStatusTransition(request.Status, status)))
        {
            return false;
        }

        if (responsibleUserId.HasValue)
        {
            var responsibleUserCanBeKept = await _dbContext.Users
                .AnyAsync(user =>
                    user.Id == responsibleUserId.Value &&
                    ((user.IsActive && !user.IsArchived) || request.ResponsibleUserId == responsibleUserId));

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

    private static bool IsAllowedStatusTransition(RequestStatus currentStatus, RequestStatus newStatus) =>
        (currentStatus, newStatus) switch
        {
            (RequestStatus.New, RequestStatus.InProgress or RequestStatus.Cancelled) => true,
            (RequestStatus.InProgress, RequestStatus.WaitingForClient or RequestStatus.Completed or RequestStatus.Cancelled) => true,
            (RequestStatus.WaitingForClient, RequestStatus.InProgress or RequestStatus.Completed or RequestStatus.Cancelled) => true,
            _ => false
        };
}
