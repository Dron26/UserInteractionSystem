using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Tests.Infrastructure;

namespace UserInteractionSystem.Tests.Application.Services;

public sealed class ClientServiceTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Theory]
    [MemberData(nameof(LengthBoundaryCases))]
    public async Task AddClientAsync_ValidatesFieldLengthBoundaries(
        ClientField field,
        int length,
        bool expectedSuccess)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var principal = await CreatePrincipalAsync(dbContext);
        var client = CreateClient();
        SetFieldValue(client, field, CreateFieldValue(field, length));

        var result = await _fixture.CreateClientService(dbContext).AddClientAsync(client, principal);

        Assert.Equal(expectedSuccess, result);
        await using var verificationContext = _fixture.CreateDbContext();
        var savedClients = verificationContext.Clients.ToList();
        if (expectedSuccess)
        {
            var savedClient = Assert.Single(savedClients);
            Assert.Equal(CreateFieldValue(field, length), GetFieldValue(savedClient, field));
        }
        else
        {
            Assert.Empty(savedClients);
        }
    }

    [Theory]
    [MemberData(nameof(LengthBoundaryCases))]
    public async Task UpdateClientAsync_ValidatesFieldLengthBoundaries(
        ClientField field,
        int length,
        bool expectedSuccess)
    {
        await using var dbContext = _fixture.CreateDbContext();
        var principal = await CreatePrincipalAsync(dbContext);
        var existingClient = CreateClient();
        dbContext.Clients.Add(existingClient);
        await dbContext.SaveChangesAsync();

        var updatedClient = new Client
        {
            Id = existingClient.Id,
            FullName = "Updated Client",
            CreatedAt = existingClient.CreatedAt
        };
        SetFieldValue(updatedClient, field, CreateFieldValue(field, length));

        var result = await _fixture.CreateClientService(dbContext).UpdateClientAsync(updatedClient, principal);

        Assert.Equal(expectedSuccess, result);
        await using var verificationContext = _fixture.CreateDbContext();
        var savedClient = await verificationContext.Clients.SingleAsync();
        if (expectedSuccess)
        {
            Assert.Equal(CreateFieldValue(field, length), GetFieldValue(savedClient, field));
        }
        else
        {
            Assert.Equal("Existing Client", savedClient.FullName);
            Assert.Null(savedClient.Phone);
            Assert.Null(savedClient.Email);
        }
    }

    public static IEnumerable<object[]> LengthBoundaryCases() =>
    [
        [ClientField.FullName, 150, true],
        [ClientField.FullName, 151, false],
        [ClientField.Phone, 30, true],
        [ClientField.Phone, 31, false],
        [ClientField.Email, 254, true],
        [ClientField.Email, 255, false]
    ];

    private static async Task<ClaimsPrincipal> CreatePrincipalAsync(
        UserInteractionSystem.Infrastructure.Data.ApplicationDbContext dbContext)
    {
        var user = new User
        {
            UserName = "client-test-user",
            PasswordHash = "not-used-by-tests",
            FullName = "Client Test User",
            Role = UserRole.Employee,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var identity = new ClaimsIdentity(
        [
            new Claim("UserId", user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        ],
        authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private static Client CreateClient() => new()
    {
        FullName = "Existing Client",
        CreatedAt = DateTime.UtcNow
    };

    private static string CreateFieldValue(ClientField field, int length) => field switch
    {
        ClientField.FullName => new string('N', length),
        ClientField.Phone => new string('1', length),
        ClientField.Email => CreateEmail(length),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    private static string CreateEmail(int length)
    {
        const int fixedLength = 64 + 1 + 63 + 1 + 63 + 1 + 4;
        var finalLabelLength = length - fixedLength;
        return $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', finalLabelLength)}.com";
    }

    private static void SetFieldValue(Client client, ClientField field, string value)
    {
        switch (field)
        {
            case ClientField.FullName:
                client.FullName = value;
                break;
            case ClientField.Phone:
                client.Phone = value;
                break;
            case ClientField.Email:
                client.Email = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    private static string? GetFieldValue(Client client, ClientField field) => field switch
    {
        ClientField.FullName => client.FullName,
        ClientField.Phone => client.Phone,
        ClientField.Email => client.Email,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
    };

    public enum ClientField
    {
        FullName,
        Phone,
        Email
    }
}
