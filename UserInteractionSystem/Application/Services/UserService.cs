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

    public async Task<bool> AddUserAsync(UserCreateModel model)
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

        await _dbContext.Users.AddAsync(user);
        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateUserAsync(int userId, string? fullName, string? userName)
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

        user.FullName = fullName.Trim();
        user.UserName = userName.Trim();
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetUserActiveAsync(int userId, bool isActive)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return false;
        }

        if (user.IsActive == isActive)
        {
            return true;
        }

        user.IsActive = isActive;
        return await _dbContext.SaveChangesAsync() > 0;
    }
}
