using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Models;
using UserInteractionSystem.Application.Services;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Infrastructure.Data;
using UserInteractionSystem.Tests.Infrastructure;
using Xunit;

namespace UserInteractionSystem.Tests.Application.Services;

public sealed class InteractionServiceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task AddInteractionAsync_OpenRequest_SavesNormalizedInteractionAndMoscowTime()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var (request, user, principal) = await CreateScenarioAsync(dbContext, RequestStatus.InProgress);
        var contactedAtMoscow = new DateTime(2026, 11, 18, 14, 25, 30, DateTimeKind.Unspecified);

        var result = await _fixture.CreateInteractionService(dbContext).AddInteractionAsync(
            request.Id,
            principal,
            InteractionChannel.Telegram,
            "  Ответили клиенту по заявке  ",
            contactedAtMoscow);

        Assert.True(result);
        await using var verificationContext = _fixture.CreateDbContext();
        var savedInteraction = await verificationContext.Interactions.SingleAsync();
        Assert.Equal(request.Id, savedInteraction.RequestId);
        Assert.Equal(user.Id, savedInteraction.UserId);
        Assert.Equal(InteractionChannel.Telegram, savedInteraction.Channel);
        Assert.Equal("Ответили клиенту по заявке", savedInteraction.Content);
        Assert.Equal(InteractionTimeZone.ToUtc(contactedAtMoscow), InteractionTimeZone.AsUtc(savedInteraction.CreatedAt));

        var history = await _fixture.CreateInteractionService(verificationContext)
            .GetInteractionsByRequestIdAsync(request.Id);
        var displayedMoscowTime = InteractionTimeZone.ToMoscowTime(history.Single().CreatedAt);
        Assert.Equal(contactedAtMoscow, DateTime.SpecifyKind(displayedMoscowTime, DateTimeKind.Unspecified));
        Assert.Equal(user.FullName, history.Single().UserName);
    }

    [Fact]
    public async Task AddInteractionAsync_InvalidContentOrChannel_RejectsEveryInvalidInput()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var (request, _, principal) = await CreateScenarioAsync(dbContext, RequestStatus.InProgress);
        var invalidInputs = new (string Content, InteractionChannel Channel)[]
        {
            (" \t ", InteractionChannel.Phone),
            (new string('x', 5001), InteractionChannel.Phone),
            ("Содержимое", (InteractionChannel)int.MaxValue)
        };
        var service = _fixture.CreateInteractionService(dbContext);

        foreach (var (content, channel) in invalidInputs)
        {
            var result = await service.AddInteractionAsync(
                request.Id,
                principal,
                channel,
                content,
                new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Unspecified));
            Assert.False(result, $"Invalid input was accepted: channel={channel}, length={content.Length}.");
        }

        await using var verificationContext = _fixture.CreateDbContext();
        Assert.Empty(await verificationContext.Interactions.ToListAsync());
    }

    [Fact]
    public async Task AddInteractionAsync_MissingOrClosedRequest_RejectsWithoutSaving()
    {
        await using var dbContext = _fixture.CreateDbContext();
        var (openRequest, _, principal) = await CreateScenarioAsync(dbContext, RequestStatus.InProgress);
        var closedRequests = new[]
        {
            new Request
            {
                ClientId = openRequest.ClientId,
                Title = "Завершённая заявка",
                Description = "Описание завершённой заявки",
                Status = RequestStatus.Completed,
                Priority = RequestPriority.Normal,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Request
            {
                ClientId = openRequest.ClientId,
                Title = "Отменённая заявка",
                Description = "Описание отменённой заявки",
                Status = RequestStatus.Cancelled,
                Priority = RequestPriority.Normal,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };
        dbContext.Requests.AddRange(closedRequests);
        await dbContext.SaveChangesAsync();
        var service = _fixture.CreateInteractionService(dbContext);
        var contactedAt = new DateTime(2026, 11, 18, 14, 0, 0, DateTimeKind.Unspecified);

        var missingRequestResult = await service.AddInteractionAsync(
            int.MaxValue,
            principal,
            InteractionChannel.Phone,
            "Контакт с отсутствующей заявкой",
            contactedAt);
        Assert.False(missingRequestResult);

        foreach (var closedRequest in closedRequests)
        {
            var result = await service.AddInteractionAsync(
                closedRequest.Id,
                principal,
                InteractionChannel.Email,
                "Контакт после закрытия",
                contactedAt);
            Assert.False(result, $"Interaction was accepted for {closedRequest.Status} request.");
        }

        await using var verificationContext = _fixture.CreateDbContext();
        Assert.Empty(await verificationContext.Interactions.ToListAsync());
    }

    [Fact]
    public async Task AddInteractionAsync_ConcurrentRequestCompletionAndInteraction_LeavesConsistentDatabaseState()
    {
        await using var seedContext = _fixture.CreateDbContext();
        var (request, _, principal) = await CreateScenarioAsync(seedContext, RequestStatus.InProgress);
        var requestId = request.Id;
        var expectedUpdatedAt = request.UpdatedAt;
        var readyCount = 0;
        var bothReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contactedAtMoscow = new DateTime(2026, 11, 18, 14, 30, 0, DateTimeKind.Unspecified);

        async Task<bool> AddInteractionAsync()
        {
            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateInteractionService(context);
            SignalReadyAndWait();
            await startGate.Task;
            return await service.AddInteractionAsync(
                requestId,
                principal,
                InteractionChannel.Email,
                "Одновременный контакт",
                contactedAtMoscow);
        }

        async Task<RequestUpdateResult> CompleteRequestAsync()
        {
            await using var context = _fixture.CreateDbContext();
            var service = _fixture.CreateRequestService(context);
            SignalReadyAndWait();
            await startGate.Task;
            return await service.UpdateRequestAsync(
                requestId,
                expectedUpdatedAt,
                RequestStatus.Completed,
                request.Priority,
                null,
                principal);
        }

        void SignalReadyAndWait()
        {
            if (Interlocked.Increment(ref readyCount) == 2)
            {
                bothReady.TrySetResult();
            }
        }

        var interactionTask = AddInteractionAsync();
        var completionTask = CompleteRequestAsync();
        await bothReady.Task;
        startGate.TrySetResult();
        var interactionSaved = await interactionTask;
        var completionResult = await completionTask;

        Assert.Contains(completionResult, new[]
        {
            RequestUpdateResult.Success,
            RequestUpdateResult.TemporaryFailure,
            RequestUpdateResult.Conflict
        });

        await using var verificationContext = _fixture.CreateDbContext();
        var finalRequest = await verificationContext.Requests.SingleAsync(item => item.Id == requestId);
        var interactions = await verificationContext.Interactions
            .Where(item => item.RequestId == requestId)
            .ToListAsync();

        if (completionResult == RequestUpdateResult.Success)
        {
            Assert.Equal(RequestStatus.Completed, finalRequest.Status);
        }
        else
        {
            Assert.Equal(RequestStatus.InProgress, finalRequest.Status);
        }

        Assert.Equal(interactionSaved ? 1 : 0, interactions.Count);
        if (interactionSaved)
        {
            Assert.Equal("Одновременный контакт", interactions[0].Content);
            Assert.Equal(InteractionTimeZone.ToUtc(contactedAtMoscow), InteractionTimeZone.AsUtc(interactions[0].CreatedAt));
        }
    }

    private static async Task<(Request Request, User User, ClaimsPrincipal Principal)> CreateScenarioAsync(
        ApplicationDbContext dbContext,
        RequestStatus status)
    {
        var client = new Client
        {
            FullName = "Клиент взаимодействий",
            CreatedAt = DateTime.UtcNow
        };
        var user = new User
        {
            UserName = $"interaction-user-{Guid.NewGuid():N}",
            PasswordHash = "not-used-by-tests",
            FullName = "Автор взаимодействия",
            Role = UserRole.Employee,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var request = new Request
        {
            ClientId = 0,
            Title = "Заявка для взаимодействия",
            Description = "Описание заявки",
            Status = status,
            Priority = RequestPriority.Normal,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Clients.Add(client);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        request.ClientId = client.Id;
        dbContext.Requests.Add(request);
        await dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim("UserId", user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        ],
        authenticationType: "Test");
        return (request, user, new ClaimsPrincipal(identity));
    }
}
