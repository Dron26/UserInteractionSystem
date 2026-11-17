using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using UserInteractionSystem.Application.Services;

namespace UserInteractionSystem.Components;

public sealed class UserRevalidatingAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserRevalidatingAuthenticationStateProvider> _logger;

    public UserRevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        ILogger<UserRevalidatingAuthenticationStateProvider> logger,
        IServiceScopeFactory scopeFactory)
        : base(loggerFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public bool SessionWasInvalidated { get; private set; }

    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        if (authenticationState.User.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            var isCurrent = await userService.IsAuthenticatedUserCurrentAsync(
                authenticationState.User,
                allowPendingPasswordChange: true);
            if (!isCurrent)
            {
                SessionWasInvalidated = true;
            }

            return isCurrent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to revalidate the signed-in user; ending the session for safety.");
            SessionWasInvalidated = true;
            return false;
        }
    }
}
