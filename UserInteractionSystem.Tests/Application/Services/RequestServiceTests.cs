using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Models;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Tests.Infrastructure;
using Xunit;

namespace UserInteractionSystem.Tests.Application.Services;

public sealed class RequestServiceTests : IAsyncLifetime
{
    private static readonly (RequestStatus Current, RequestStatus Next)[] AllowedTransitions =
    [
        (RequestStatus.New, RequestStatus.InProgress),
        (RequestStatus.New, RequestStatus.Cancelled),
        (RequestStatus.InProgress, RequestStatus.WaitingForClient),
        (RequestStatus.InProgress, RequestStatus.Completed),
        (RequestStatus.InProgress, RequestStatus.Cancelled),
        (RequestStatus.WaitingForClient, RequestStatus.InProgress),
        (RequestStatus.WaitingForClient, RequestStatus.Completed),
        (RequestStatus.WaitingForClient, RequestStatus.Cancelled)
    ];

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task AddRequestAsync_ActiveClient_CreatesRequestWithInitialValues()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var (client, _, principal) = await CreateActorsAsync(dbContext);
        var now = DateTime.UtcNow;
        var request = new Request
        {
            ClientId = client.Id,
            Title = "  Запрос на обслуживание  ",
            Description = "  Описание заявки  ",
            Status = RequestStatus.Cancelled,
            Priority = RequestPriority.Urgent,
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now.AddDays(-1),
            CompletedAt = now.AddDays(-1)
        };

        var result = await _fixture.CreateRequestService(dbContext).AddRequestAsync(request, principal);

        Assert.True(result);
        await using var verificationContext = _fixture.CreateDbContext();
        var savedRequest = await verificationContext.Requests.SingleAsync();
        Assert.Equal("Запрос на обслуживание", savedRequest.Title);
        Assert.Equal("Описание заявки", savedRequest.Description);
        Assert.Equal(client.Id, savedRequest.ClientId);
        Assert.Equal(RequestStatus.New, savedRequest.Status);
        Assert.Equal(RequestPriority.Normal, savedRequest.Priority);
        Assert.InRange(savedRequest.CreatedAt, now, DateTime.UtcNow);
        Assert.Equal(savedRequest.CreatedAt, savedRequest.UpdatedAt);
        Assert.Null(savedRequest.CompletedAt);
    }

    [Fact]
    public async Task AddRequestAsync_MissingOrArchivedClient_RejectsWithoutPersistingRequests()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var (_, _, principal) = await CreateActorsAsync(dbContext);
        var archivedClient = new Client
        {
            FullName = "Архивный клиент",
            IsArchived = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Clients.Add(archivedClient);
        await dbContext.SaveChangesAsync();

        var service = _fixture.CreateRequestService(dbContext);
        var missingClientResult = await service.AddRequestAsync(CreateRequest(int.MaxValue), principal);
        var archivedClientResult = await service.AddRequestAsync(CreateRequest(archivedClient.Id), principal);

        Assert.False(missingClientResult);
        Assert.False(archivedClientResult);
        await using var verificationContext = _fixture.CreateDbContext();
        Assert.Empty(await verificationContext.Requests.ToListAsync());
    }

    [Fact]
    public async Task UpdateRequestAsync_StatusTransition_EnforcesAllTwentyTransitionsBetweenDifferentStatuses()
    {
        await using var seedContext = _fixture.CreateDbContext();
        var (client, _, principal) = await CreateActorsAsync(seedContext);
        var requests = (
            from currentStatus in Enum.GetValues<RequestStatus>()
            from nextStatus in Enum.GetValues<RequestStatus>()
            where currentStatus != nextStatus
            select (Current: currentStatus, Next: nextStatus, Request: CreateRequest(client.Id, currentStatus)))
            .ToList();
        seedContext.Requests.AddRange(requests.Select(item => item.Request));
        await seedContext.SaveChangesAsync();

        await using var updateContext = _fixture.CreateDbContext();
        var service = _fixture.CreateRequestService(updateContext);
        foreach (var transition in requests)
        {
            var expectedUpdatedAt = transition.Request.UpdatedAt;
            var isAllowed = AllowedTransitions.Contains((transition.Current, transition.Next));
            var result = await service.UpdateRequestAsync(
                transition.Request.Id,
                expectedUpdatedAt,
                transition.Next,
                transition.Request.Priority,
                null,
                principal);

            await using var verificationContext = _fixture.CreateDbContext();
            var savedRequest = await verificationContext.Requests
                .AsNoTracking()
                .SingleAsync(item => item.Id == transition.Request.Id);
            var scenario = $"{transition.Current} -> {transition.Next}";
            if (isAllowed)
            {
                Assert.True(result == RequestUpdateResult.Success, $"{scenario}: expected success, got {result}.");
                Assert.Equal(transition.Next, savedRequest.Status);
            }
            else
            {
                Assert.True(result == RequestUpdateResult.InvalidData, $"{scenario}: expected InvalidData, got {result}.");
                Assert.Equal(transition.Current, savedRequest.Status);
                Assert.Equal(expectedUpdatedAt, savedRequest.UpdatedAt);
            }
        }
    }

    [Fact]
    public async Task UpdateRequestAsync_TransitionToCompleted_SetsCompletedAtAndCompletedRequestCannotBeReopened()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var (client, _, principal) = await CreateActorsAsync(dbContext);
        var request = CreateRequest(client.Id, RequestStatus.InProgress);
        dbContext.Requests.Add(request);
        await dbContext.SaveChangesAsync();

        var result = await _fixture.CreateRequestService(dbContext).UpdateRequestAsync(
            request.Id,
            request.UpdatedAt,
            RequestStatus.Completed,
            request.Priority,
            null,
            principal);

        Assert.Equal(RequestUpdateResult.Success, result);
        await using var verificationContext = _fixture.CreateDbContext();
        var completedRequest = await verificationContext.Requests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(RequestStatus.Completed, completedRequest.Status);
        Assert.NotNull(completedRequest.CompletedAt);
        var completedAt = completedRequest.CompletedAt!.Value;

        var reopenResult = await _fixture.CreateRequestService(dbContext).UpdateRequestAsync(
            request.Id,
            completedRequest.UpdatedAt,
            RequestStatus.InProgress,
            completedRequest.Priority,
            null,
            principal);

        Assert.Equal(RequestUpdateResult.InvalidData, reopenResult);
        await using var finalContext = _fixture.CreateDbContext();
        var finalRequest = await finalContext.Requests.SingleAsync(item => item.Id == request.Id);
        Assert.Equal(RequestStatus.Completed, finalRequest.Status);
        Assert.Equal(completedAt, finalRequest.CompletedAt);
    }

    [Fact]
    public async Task UpdateRequestAsync_StaleUpdatedAt_ReturnsConflictAndPreservesCurrentData()
    {
        await using var seedContext = _fixture.CreateDbContext();
        var (client, _, principal) = await CreateActorsAsync(seedContext);
        var request = CreateRequest(client.Id, RequestStatus.New);
        request.Priority = RequestPriority.Low;
        request.Title = "Актуальное название";
        seedContext.Requests.Add(request);
        await seedContext.SaveChangesAsync();
        var staleUpdatedAt = request.UpdatedAt.AddMinutes(-1);
        var requestId = request.Id;
        var currentUpdatedAt = request.UpdatedAt;

        await using var updateContext = _fixture.CreateDbContext();
        var result = await _fixture.CreateRequestService(updateContext).UpdateRequestAsync(
            requestId,
            staleUpdatedAt,
            RequestStatus.InProgress,
            RequestPriority.High,
            null,
            principal);

        Assert.Equal(RequestUpdateResult.Conflict, result);
        await using var verificationContext = _fixture.CreateDbContext();
        var savedRequest = await verificationContext.Requests.SingleAsync(item => item.Id == requestId);
        Assert.Equal("Актуальное название", savedRequest.Title);
        Assert.Equal(RequestStatus.New, savedRequest.Status);
        Assert.Equal(RequestPriority.Low, savedRequest.Priority);
        Assert.Equal(currentUpdatedAt, savedRequest.UpdatedAt);
    }

    [Fact]
    public async Task AddRequestAsync_InactiveOrArchivedAssignees_RejectsEachRequest()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var (client, _, principal) = await CreateActorsAsync(dbContext);
        var unavailableAssignees = new[]
        {
            new User
            {
                UserName = $"inactive-{Guid.NewGuid():N}",
                PasswordHash = "not-used-by-tests",
                FullName = "Неактивный сотрудник",
                Role = UserRole.Employee,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                UserName = $"archived-{Guid.NewGuid():N}",
                PasswordHash = "not-used-by-tests",
                FullName = "Архивный сотрудник",
                Role = UserRole.Employee,
                IsActive = true,
                IsArchived = true,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                UserName = $"inactive-archived-{Guid.NewGuid():N}",
                PasswordHash = "not-used-by-tests",
                FullName = "Неактивный архивный сотрудник",
                Role = UserRole.Employee,
                IsActive = false,
                IsArchived = true,
                CreatedAt = DateTime.UtcNow
            }
        };
        dbContext.Users.AddRange(unavailableAssignees);
        await dbContext.SaveChangesAsync();

        var service = _fixture.CreateRequestService(dbContext);
        foreach (var assignee in unavailableAssignees)
        {
            var request = CreateRequest(client.Id);
            request.ResponsibleUserId = assignee.Id;
            var result = await service.AddRequestAsync(request, principal);
            Assert.False(result, $"Request was accepted for unavailable assignee {assignee.UserName}.");
        }

        await using var verificationContext = _fixture.CreateDbContext();
        Assert.Empty(await verificationContext.Requests.ToListAsync());
    }

    [Fact]
    public async Task UpdateRequestAsync_ChangesPriorityAndAssigneeWithoutChangingStatus()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var (client, _, principal) = await CreateActorsAsync(dbContext);
        var newAssignee = new User
        {
            UserName = $"assignee-{Guid.NewGuid():N}",
            PasswordHash = "not-used-by-tests",
            FullName = "Новый ответственный",
            Role = UserRole.Employee,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(newAssignee);
        var request = CreateRequest(client.Id, RequestStatus.New);
        dbContext.Requests.Add(request);
        await dbContext.SaveChangesAsync();
        var requestId = request.Id;
        var expectedUpdatedAt = request.UpdatedAt;

        var result = await _fixture.CreateRequestService(dbContext).UpdateRequestAsync(
            requestId,
            expectedUpdatedAt,
            RequestStatus.New,
            RequestPriority.High,
            newAssignee.Id,
            principal);

        Assert.Equal(RequestUpdateResult.Success, result);
        await using var verificationContext = _fixture.CreateDbContext();
        var updatedRequest = await verificationContext.Requests.SingleAsync(item => item.Id == requestId);
        Assert.Equal(RequestStatus.New, updatedRequest.Status);
        Assert.Equal(RequestPriority.High, updatedRequest.Priority);
        Assert.Equal(newAssignee.Id, updatedRequest.ResponsibleUserId);
    }

    [Fact]
    public async Task UpdateRequestAsync_StoredUpdatedAtTextFormats_AcceptsMatchingUtcInstant()
    {
        await using var seedContext = _fixture.CreateDbContext();
        var (client, _, principal) = await CreateActorsAsync(seedContext);
        var expectedUpdatedAt = new DateTime(2026, 11, 18, 20, 30, 45, DateTimeKind.Utc);
        var storedTimestamps = new[]
        {
            "2026-11-18 20:30:45.0000000",
            "2026-11-18T20:30:45.0000000+00:00"
        };
        var requests = storedTimestamps.Select(_ => CreateRequest(client.Id)).ToList();
        foreach (var request in requests)
        {
            request.UpdatedAt = expectedUpdatedAt;
        }

        seedContext.Requests.AddRange(requests);
        await seedContext.SaveChangesAsync();
        for (var index = 0; index < requests.Count; index++)
        {
            await seedContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Requests\" SET \"UpdatedAt\" = {storedTimestamps[index]} WHERE \"Id\" = {requests[index].Id}");
        }

        await using var updateContext = _fixture.CreateDbContext();
        var service = _fixture.CreateRequestService(updateContext);
        foreach (var request in requests)
        {
            var result = await service.UpdateRequestAsync(
                request.Id,
                expectedUpdatedAt,
                RequestStatus.InProgress,
                RequestPriority.Normal,
                null,
                principal);

            Assert.Equal(RequestUpdateResult.Success, result);
            await using var verificationContext = _fixture.CreateDbContext();
            var updatedRequest = await verificationContext.Requests
                .AsNoTracking()
                .SingleAsync(item => item.Id == request.Id);
            Assert.Equal(RequestStatus.InProgress, updatedRequest.Status);
        }
    }

    [Fact]
    public async Task UpdateRequestAsync_ConcurrentUpdatesFromSameVersion_OnlyOneChangeIsPersisted()
    {
        await using var seedContext = _fixture.CreateDbContext();
        var (client, _, principal) = await CreateActorsAsync(seedContext);
        var request = CreateRequest(client.Id, RequestStatus.New);
        request.Priority = RequestPriority.Low;
        seedContext.Requests.Add(request);
        await seedContext.SaveChangesAsync();
        var requestId = request.Id;
        var expectedUpdatedAt = request.UpdatedAt;
        var readyCount = 0;
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<RequestUpdateResult> UpdateAsync(RequestStatus targetStatus)
        {
            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateRequestService(context);
            if (Interlocked.Increment(ref readyCount) == 2)
            {
                bothReady.TrySetResult();
            }

            await startGate.Task;
            return await service.UpdateRequestAsync(
                requestId,
                expectedUpdatedAt,
                targetStatus,
                RequestPriority.High,
                null,
                principal);
        }

        var firstUpdate = UpdateAsync(RequestStatus.InProgress);
        var secondUpdate = UpdateAsync(RequestStatus.Cancelled);
        await bothReady.Task;
        startGate.TrySetResult();
        var results = await Task.WhenAll(firstUpdate, secondUpdate);

        Assert.Equal(1, results.Count(result => result == RequestUpdateResult.Success));
        Assert.All(results, result => Assert.Contains(
            new[]
            {
                RequestUpdateResult.Success,
                RequestUpdateResult.Conflict,
                RequestUpdateResult.TemporaryFailure
            },
            acceptedResult => acceptedResult == result));

        await using var verificationContext = _fixture.CreateDbContext();
        var savedRequest = await verificationContext.Requests.SingleAsync(item => item.Id == requestId);
        Assert.Contains(
            new[] { RequestStatus.InProgress, RequestStatus.Cancelled },
            allowedStatus => allowedStatus == savedRequest.Status);
        Assert.Equal(RequestPriority.High, savedRequest.Priority);
        Assert.NotEqual(expectedUpdatedAt, savedRequest.UpdatedAt);
    }

    private static async Task<(Client Client, User User, ClaimsPrincipal Principal)> CreateActorsAsync(
        UserInteractionSystem.Infrastructure.Data.ApplicationDbContext dbContext)
    {
        var client = new Client
        {
            FullName = "Тестовый клиент",
            CreatedAt = DateTime.UtcNow
        };
        var user = new User
        {
            UserName = "request-test-user",
            PasswordHash = "not-used-by-tests",
            FullName = "Тестовый сотрудник",
            Role = UserRole.Employee,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Clients.Add(client);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim("UserId", user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        ],
        authenticationType: "Test");
        return (client, user, new ClaimsPrincipal(identity));
    }

    private static Request CreateRequest(int clientId, RequestStatus status = RequestStatus.New) => new()
    {
        ClientId = clientId,
        Title = "Тестовая заявка",
        Description = "Описание тестовой заявки",
        Status = status,
        Priority = RequestPriority.Normal,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
