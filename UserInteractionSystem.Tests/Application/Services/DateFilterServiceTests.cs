using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Models;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Tests.Infrastructure;

namespace UserInteractionSystem.Tests.Application.Services;

public sealed class DateFilterServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixedTimeProvider Clock = new(FixedNow);
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Today_FiltersClientsRequestsAndInteractionsByMoscowDayBoundaries()
    {
        var startUtc = new DateTime(2026, 11, 18, 21, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddDays(1);
        var records = await SeedAsync(
        [
            startUtc.AddSeconds(-1),
            startUtc,
            endUtc.AddSeconds(-1),
            endUtc
        ]);

        var result = await QueryAllAsync(
            records,
            ClientDatePeriod.Today,
            RequestDatePeriod.Today,
            InteractionDatePeriod.Today);
        var expected = new[] { startUtc.Ticks, endUtc.AddSeconds(-1).Ticks };

        Assert.Equal(expected, result.Clients);
        Assert.Equal(expected, result.Requests);
        Assert.Equal(expected, result.Interactions);
    }

    [Fact]
    public async Task Custom_UsesMoscowCalendarDatesForSingleAndMultipleDays()
    {
        var firstDayStartUtc = new DateTime(2026, 11, 17, 21, 0, 0, DateTimeKind.Utc);
        var secondDayStartUtc = firstDayStartUtc.AddDays(1);
        var thirdDayStartUtc = secondDayStartUtc.AddDays(1);
        var records = await SeedAsync(
        [
            firstDayStartUtc.AddSeconds(-1),
            firstDayStartUtc,
            firstDayStartUtc.AddHours(5),
            secondDayStartUtc.AddSeconds(-1),
            secondDayStartUtc,
            thirdDayStartUtc.AddSeconds(-1),
            thirdDayStartUtc
        ]);
        var fromDate = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Unspecified);
        var nextDate = fromDate.AddDays(1);

        var oneDay = await QueryAllAsync(
            records,
            ClientDatePeriod.Custom,
            RequestDatePeriod.Custom,
            InteractionDatePeriod.Custom,
            fromDate,
            fromDate);
        var twoDays = await QueryAllAsync(
            records,
            ClientDatePeriod.Custom,
            RequestDatePeriod.Custom,
            InteractionDatePeriod.Custom,
            fromDate,
            nextDate);
        var expectedOneDay = new[]
        {
            firstDayStartUtc.Ticks,
            firstDayStartUtc.AddHours(5).Ticks,
            secondDayStartUtc.AddSeconds(-1).Ticks
        };
        var expectedTwoDays = expectedOneDay
            .Append(secondDayStartUtc.Ticks)
            .Append(thirdDayStartUtc.AddSeconds(-1).Ticks)
            .ToArray();

        Assert.Equal(expectedOneDay, oneDay.Clients);
        Assert.Equal(expectedOneDay, oneDay.Requests);
        Assert.Equal(expectedOneDay, oneDay.Interactions);
        Assert.Equal(expectedTwoDays, twoDays.Clients);
        Assert.Equal(expectedTwoDays, twoDays.Requests);
        Assert.Equal(expectedTwoDays, twoDays.Interactions);
    }

    [Fact]
    public async Task Custom_ReversedRangeReturnsNoResultsAndMissingBoundaryRemainsOpenEnded()
    {
        var firstDayStartUtc = new DateTime(2026, 11, 17, 21, 0, 0, DateTimeKind.Utc);
        var secondDayStartUtc = firstDayStartUtc.AddDays(1);
        var thirdDayStartUtc = secondDayStartUtc.AddDays(1);
        var records = await SeedAsync(
        [
            firstDayStartUtc.AddSeconds(-1),
            firstDayStartUtc,
            secondDayStartUtc,
            thirdDayStartUtc
        ]);
        var fromDate = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Unspecified);
        var toDate = fromDate.AddDays(1);

        var reversed = await QueryAllAsync(
            records,
            ClientDatePeriod.Custom,
            RequestDatePeriod.Custom,
            InteractionDatePeriod.Custom,
            toDate,
            fromDate);
        var fromOnly = await QueryAllAsync(
            records,
            ClientDatePeriod.Custom,
            RequestDatePeriod.Custom,
            InteractionDatePeriod.Custom,
            fromDate,
            null);
        var toOnly = await QueryAllAsync(
            records,
            ClientDatePeriod.Custom,
            RequestDatePeriod.Custom,
            InteractionDatePeriod.Custom,
            null,
            fromDate);

        Assert.Empty(reversed.Clients);
        Assert.Empty(reversed.Requests);
        Assert.Empty(reversed.Interactions);

        var expectedFromOnly = new[]
        {
            firstDayStartUtc.Ticks,
            secondDayStartUtc.Ticks,
            thirdDayStartUtc.Ticks
        };
        var expectedToOnly = new[] { firstDayStartUtc.AddSeconds(-1).Ticks, firstDayStartUtc.Ticks };
        Assert.Equal(expectedFromOnly, fromOnly.Clients);
        Assert.Equal(expectedFromOnly, fromOnly.Requests);
        Assert.Equal(expectedFromOnly, fromOnly.Interactions);
        Assert.Equal(expectedToOnly, toOnly.Clients);
        Assert.Equal(expectedToOnly, toOnly.Requests);
        Assert.Equal(expectedToOnly, toOnly.Interactions);
    }

    [Fact]
    public async Task LastWeekAndLastMonth_RetainSlidingUtcWindows()
    {
        var nowUtc = FixedNow.UtcDateTime;
        var weekStartUtc = nowUtc.AddDays(-7);
        var monthStartUtc = nowUtc.AddMonths(-1);
        var records = await SeedAsync(
        [
            monthStartUtc.AddSeconds(-1),
            monthStartUtc,
            weekStartUtc.AddSeconds(-1),
            weekStartUtc,
            nowUtc.AddSeconds(-1),
            nowUtc
        ]);

        var lastWeek = await QueryAllAsync(
            records,
            ClientDatePeriod.LastWeek,
            RequestDatePeriod.LastWeek,
            InteractionDatePeriod.Last7Days);
        var lastMonth = await QueryAllAsync(
            records,
            ClientDatePeriod.LastMonth,
            RequestDatePeriod.LastMonth,
            InteractionDatePeriod.LastMonth);
        var expectedWeek = new[] { weekStartUtc.Ticks, nowUtc.AddSeconds(-1).Ticks };
        var expectedMonth = new[]
        {
            monthStartUtc.Ticks,
            weekStartUtc.AddSeconds(-1).Ticks,
            weekStartUtc.Ticks,
            nowUtc.AddSeconds(-1).Ticks
        };

        Assert.Equal(expectedWeek, lastWeek.Clients);
        Assert.Equal(expectedWeek, lastWeek.Requests);
        Assert.Equal(expectedWeek, lastWeek.Interactions);
        Assert.Equal(expectedMonth, lastMonth.Clients);
        Assert.Equal(expectedMonth, lastMonth.Requests);
        Assert.Equal(expectedMonth, lastMonth.Interactions);
    }

    private async Task<SeededRecords> SeedAsync(DateTime[] timestampsUtc)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var user = new User
        {
            UserName = $"date-filter-user-{Guid.NewGuid():N}",
            PasswordHash = "not-used-by-tests",
            FullName = "Date Filter User",
            Role = UserRole.Employee,
            IsActive = true,
            CreatedAt = FixedNow.UtcDateTime
        };
        dbContext.Users.Add(user);

        var clients = timestampsUtc.Select((timestamp, index) => new Client
        {
            FullName = $"Date Filter Client {index}",
            CreatedAt = timestamp
        }).ToArray();
        dbContext.Clients.AddRange(clients);
        await dbContext.SaveChangesAsync();

        var requests = timestampsUtc.Select((timestamp, index) => new Request
        {
            ClientId = clients[index].Id,
            Title = $"Date Filter Request {index}",
            Description = "Date filter test request",
            Status = RequestStatus.New,
            Priority = RequestPriority.Normal,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        }).ToArray();
        dbContext.Requests.AddRange(requests);
        await dbContext.SaveChangesAsync();

        var interactions = timestampsUtc.Select((timestamp, index) => new Interaction
        {
            RequestId = requests[index].Id,
            UserId = user.Id,
            Channel = InteractionChannel.Email,
            Content = $"Date filter interaction {index}",
            CreatedAt = timestamp
        }).ToArray();
        dbContext.Interactions.AddRange(interactions);
        await dbContext.SaveChangesAsync();

        return new SeededRecords(requests.Select(request => request.Id).ToArray());
    }

    private async Task<(long[] Clients, long[] Requests, long[] Interactions)> QueryAllAsync(
        SeededRecords records,
        ClientDatePeriod clientPeriod,
        RequestDatePeriod requestPeriod,
        InteractionDatePeriod interactionPeriod,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var clients = await _fixture.CreateClientService(dbContext, Clock).GetClientsAsync(new ClientListFilter
        {
            Period = clientPeriod,
            FromDate = fromDate,
            ToDate = toDate
        });
        var requests = await _fixture.CreateRequestService(dbContext, Clock).GetRequestListAsync(new RequestListFilter
        {
            Period = requestPeriod,
            FromDate = fromDate,
            ToDate = toDate
        });
        var interactionService = _fixture.CreateInteractionService(dbContext, Clock);
        var interactions = new List<DateTime>();
        foreach (var requestId in records.RequestIds)
        {
            var history = await interactionService.GetInteractionsByRequestIdAsync(requestId, new InteractionHistoryFilter
            {
                Period = interactionPeriod,
                FromDate = fromDate,
                ToDate = toDate
            });
            interactions.AddRange(history.Select(item => item.CreatedAt));
        }

        return (
            AsUtcTicks(clients.Select(client => client.CreatedAt)),
            AsUtcTicks(requests.Select(request => request.CreatedAt)),
            AsUtcTicks(interactions));
    }

    private static long[] AsUtcTicks(IEnumerable<DateTime> values) => values
        .Select(value => DateTime.SpecifyKind(value, DateTimeKind.Utc).Ticks)
        .OrderBy(ticks => ticks)
        .ToArray();

    private sealed record SeededRecords(int[] RequestIds);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
