using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Models;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Domain.Enums;
using UserInteractionSystem.Infrastructure.Data;

namespace UserInteractionSystem.Application.Services;

public class UserService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher<User> _passwordHasher;

    public UserService(ApplicationDbContext dbContext, IPasswordHasher<User> passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<bool> HasAdministratorAsync()
    {
        return await _dbContext.Users
            .AnyAsync(user => user.Role == UserRole.Admin);
    }

    public async Task<bool> IsAuthenticatedUserCurrentAsync(
        ClaimsPrincipal? principal,
        UserRole? requiredRole = null)
    {
        var roleClaim = principal?.FindFirst(ClaimTypes.Role)?.Value;
        if (principal?.Identity?.IsAuthenticated != true ||
            !int.TryParse(
                principal.FindFirst("UserId")?.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var userId) ||
            userId <= 0 ||
            !Enum.TryParse<UserRole>(roleClaim, ignoreCase: false, out var claimedRole) ||
            !Enum.IsDefined(claimedRole) ||
            roleClaim != claimedRole.ToString())
        {
            return false;
        }

        return (requiredRole is null || claimedRole == requiredRole) &&
            await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(user =>
                    user.Id == userId &&
                    user.IsActive &&
                    user.Role == claimedRole);
    }

    public async Task<bool> CreateInitialAdministratorAsync(
        string? fullName,
        string? userName,
        string? password)
    {
        if (string.IsNullOrWhiteSpace(fullName) ||
            fullName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(userName) ||
            userName.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(password) ||
            password.Length < 8)
        {
            return false;
        }

        var user = new User
        {
            FullName = fullName.Trim(),
            UserName = userName.Trim(),
            PasswordHash = string.Empty,
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        var normalizedUserName = user.UserName.ToLowerInvariant();
        var userNameExists = await _dbContext.Users
            .AnyAsync(existingUser => existingUser.UserName.ToLower() == normalizedUserName);

        if (userNameExists)
        {
            return false;
        }

        return await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users" ("UserName", "PasswordHash", "FullName", "Role", "IsActive", "CreatedAt")
            SELECT {user.UserName}, {user.PasswordHash}, {user.FullName}, {(int)UserRole.Admin}, {user.IsActive}, {user.CreatedAt}
            WHERE NOT EXISTS (SELECT 1 FROM "Users" WHERE "Role" = {(int)UserRole.Admin})
            """) == 1;
    }

    public async Task<AuthenticatedUserItem?> AuthenticateAsync(string? userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var normalizedUserName = userName.Trim().ToLowerInvariant();
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.UserName.ToLower() == normalizedUserName);

        if (user is null)
        {
            return null;
        }

        PasswordVerificationResult verificationResult;
        try
        {
            verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        }
        catch (FormatException)
        {
            return null;
        }

        if (verificationResult == PasswordVerificationResult.Failed || !user.IsActive)
        {
            return null;
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var updatedPasswordHash = _passwordHasher.HashPassword(user, password);
            await _dbContext.Users
                .Where(candidate => candidate.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.PasswordHash, updatedPasswordHash));
        }

        return new AuthenticatedUserItem
        {
            Id = user.Id,
            UserName = user.UserName,
            Role = user.Role
        };
    }

    public async Task<List<UserListItem>> GetUsersAsync()
    {
        return await _dbContext.Users
            .AsNoTracking()
            .OrderBy(user => user.FullName)
            .Select(user => new UserListItem
            {
                Id = user.Id,
                FullName = user.FullName,
                UserName = user.UserName,
                Role = user.Role,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<bool> AddUserAsync(UserCreateModel model, ClaimsPrincipal? currentUser)
    {
        if (model is null ||
            string.IsNullOrWhiteSpace(model.FullName) ||
            model.FullName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(model.UserName) ||
            model.UserName.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(model.Password) ||
            model.Password.Length < 8 ||
            !Enum.IsDefined(model.Role))
        {
            return false;
        }

        var fullName = model.FullName.Trim();
        var userName = model.UserName.Trim();
        var normalizedUserName = userName.ToLowerInvariant();
        var userNameExists = await _dbContext.Users
            .AnyAsync(user => user.UserName.ToLower() == normalizedUserName);

        if (userNameExists)
        {
            return false;
        }

        if (!await _dbContext.Users.AnyAsync() && model.Role != UserRole.Admin)
        {
            return false;
        }

        var user = new User
        {
            FullName = fullName,
            UserName = userName,
            PasswordHash = string.Empty,
            Role = model.Role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);

        if (!await IsAuthenticatedUserCurrentAsync(currentUser, UserRole.Admin))
        {
            return false;
        }

        await _dbContext.Users.AddAsync(user);
        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateUserAsync(
        int userId,
        string? fullName,
        string? userName,
        ClaimsPrincipal? currentUser)
    {
        if (string.IsNullOrWhiteSpace(fullName) ||
            fullName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(userName) ||
            userName.Trim().Length > 100)
        {
            return false;
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return false;
        }

        var normalizedUserName = userName.Trim().ToLowerInvariant();
        var userNameExists = await _dbContext.Users
            .AnyAsync(existingUser =>
                existingUser.Id != userId &&
                existingUser.UserName.ToLower() == normalizedUserName);

        if (userNameExists)
        {
            return false;
        }

        if (!await IsAuthenticatedUserCurrentAsync(currentUser, UserRole.Admin))
        {
            return false;
        }

        user.FullName = fullName.Trim();
        user.UserName = userName.Trim();
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetUserActiveAsync(
        int userId,
        bool isActive,
        ClaimsPrincipal? currentUser)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return false;
        }

        await _dbContext.Entry(user).ReloadAsync();
        if (_dbContext.Entry(user).State == EntityState.Detached)
        {
            return false;
        }

        if (user.IsActive == isActive)
        {
            return await IsAuthenticatedUserCurrentAsync(currentUser, UserRole.Admin);
        }

        if (!await IsAuthenticatedUserCurrentAsync(currentUser, UserRole.Admin))
        {
            return false;
        }

        if (!isActive && user.Role == UserRole.Admin)
        {
            var updatedRows = await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Users"
                SET "IsActive" = 0
                WHERE "Id" = {userId}
                    AND "IsActive" <> 0
                    AND (
                        SELECT COUNT(*)
                        FROM "Users"
                        WHERE "Role" = {(int)UserRole.Admin} AND "IsActive" <> 0
                    ) > 1
                """);

            if (updatedRows == 0)
            {
                return false;
            }

            await _dbContext.Entry(user).ReloadAsync();
            return true;
        }

        user.IsActive = isActive;
        return await _dbContext.SaveChangesAsync() > 0;
    }
}
