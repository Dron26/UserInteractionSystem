using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Domain.Entities;
using Xunit;

namespace UserInteractionSystem.Tests.Infrastructure;

public sealed class SqliteDatabaseFixtureTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task TemporaryDatabase_CreatesSchemaAndPersistsClientWithoutUsingApplicationDatabase()
    {
        Assert.True(File.Exists(_fixture.DatabasePath));
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var tempRootWithSeparator = Path.EndsInDirectorySeparator(tempRoot)
            ? tempRoot
            : tempRoot + Path.DirectorySeparatorChar;
        Assert.StartsWith(
            tempRootWithSeparator,
            Path.GetFullPath(_fixture.DatabasePath),
            StringComparison.OrdinalIgnoreCase);

        var clientId = 0;
        await using (var writeContext = _fixture.CreateDbContext())
        {
            Assert.Equal(_fixture.DatabasePath, writeContext.Database.GetDbConnection().DataSource);
            Assert.True(await writeContext.Database.CanConnectAsync());

            var client = new Client
            {
                FullName = "Тестовый клиент"
            };
            writeContext.Clients.Add(client);
            await writeContext.SaveChangesAsync();
            clientId = client.Id;
        }

        await using var readContext = _fixture.CreateDbContext();
        var savedClient = await readContext.Clients.SingleAsync(client => client.Id == clientId);

        Assert.Equal("Тестовый клиент", savedClient.FullName);
        Assert.Equal(_fixture.DatabasePath, readContext.Database.GetDbConnection().DataSource);
    }

    [Fact]
    public async Task FixtureCleanup_DeletesOnlyItsOwnDatabase()
    {
        var otherFixture = new SqliteDatabaseFixture();
        await otherFixture.InitializeAsync();
        var otherDatabasePath = otherFixture.DatabasePath;

        try
        {
            Assert.NotEqual(_fixture.DatabasePath, otherDatabasePath);
        }
        finally
        {
            await otherFixture.DisposeAsync();
        }

        Assert.False(File.Exists(otherDatabasePath));
        Assert.True(File.Exists(_fixture.DatabasePath));
    }
}
