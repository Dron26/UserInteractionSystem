using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Services;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Infrastructure.Data;

namespace UserInteractionSystem.Tests.Infrastructure;

public sealed class SqliteDatabaseFixture : IAsyncLifetime
{
    private readonly ConcurrentBag<ApplicationDbContext> _dbContexts = [];
    private string? _databaseDirectory;

    public string DatabasePath { get; private set; } = string.Empty;

    public string ConnectionString
    {
        get
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Pooling = false
            };
            return builder.ToString();
        }
    }

    public async Task InitializeAsync()
    {
        _databaseDirectory = Path.Combine(
            Path.GetTempPath(),
            "UserInteractionSystem.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_databaseDirectory);
        DatabasePath = Path.Combine(_databaseDirectory, "test.db");

        var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        var dbContext = new ApplicationDbContext(options);
        _dbContexts.Add(dbContext);
        return dbContext;
    }

    public UserService CreateUserService(ApplicationDbContext dbContext) =>
        new(dbContext, new PasswordHasher<User>());

    public ClientService CreateClientService(ApplicationDbContext dbContext)
    {
        var userService = CreateUserService(dbContext);
        return new ClientService(dbContext, userService);
    }

    public RequestService CreateRequestService(ApplicationDbContext dbContext)
    {
        var userService = CreateUserService(dbContext);
        return new RequestService(dbContext, userService);
    }

    public InteractionService CreateInteractionService(ApplicationDbContext dbContext)
    {
        var userService = CreateUserService(dbContext);
        return new InteractionService(dbContext, userService);
    }

    public async Task DisposeAsync()
    {
        foreach (var dbContext in _dbContexts)
        {
            await dbContext.DisposeAsync();
        }

        if (_databaseDirectory is null)
        {
            return;
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            var filePath = DatabasePath + suffix;
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        Directory.Delete(_databaseDirectory);
        _databaseDirectory = null;
    }
}
