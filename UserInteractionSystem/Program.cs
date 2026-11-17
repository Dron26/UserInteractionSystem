using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Application.Services;
using UserInteractionSystem.Components;
using UserInteractionSystem.Domain.Entities;
using UserInteractionSystem.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
var sqliteConnectionString = new SqliteConnectionStringBuilder(connectionString);

if (!Path.IsPathRooted(sqliteConnectionString.DataSource))
{
    sqliteConnectionString.DataSource = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, sqliteConnectionString.DataSource));
}

var databaseDirectory = Path.GetDirectoryName(sqliteConnectionString.DataSource);
if (!string.IsNullOrEmpty(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(sqliteConnectionString.ConnectionString));
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<RequestService>();
builder.Services.AddScoped<InteractionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.Cookie.Name = "UserInteractionSystem.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var userService = context.HttpContext.RequestServices.GetRequiredService<UserService>();
            if (await userService.IsAuthenticatedUserCurrentAsync(
                context.Principal,
                allowPendingPasswordChange: true))
            {
                return;
            }

            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/login?session=invalid");
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<AuthenticationStateProvider, UserRevalidatingAuthenticationStateProvider>();

var app = builder.Build();



using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/account/login", async (
    HttpContext context,
    IAntiforgery antiforgery,
    UserService userService) =>
{
    if (!await IsValidAntiforgeryRequestAsync(context, antiforgery))
    {
        return Results.BadRequest();
    }

    var form = await context.Request.ReadFormAsync();
    var authenticatedUser = await userService.AuthenticateAsync(form["userName"], form["password"]);
    if (authenticatedUser is null)
    {
        return Results.Redirect("/login?error=true");
    }

    var claims = new[]
    {
        new Claim("UserId", authenticatedUser.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim("UserName", authenticatedUser.UserName),
        new Claim(ClaimTypes.Role, authenticatedUser.Role.ToString()),
        new Claim("MustChangePassword", authenticatedUser.MustChangePassword.ToString())
    };
    var identity = new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme,
        "UserName",
        ClaimTypes.Role);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    return Results.Redirect(authenticatedUser.MustChangePassword ? "/change-password" : "/");
}).AllowAnonymous();

app.MapPost("/account/change-password", async (
    HttpContext context,
    IAntiforgery antiforgery,
    UserService userService) =>
{
    if (!await IsValidAntiforgeryRequestAsync(context, antiforgery))
    {
        return Results.BadRequest();
    }

    var form = await context.Request.ReadFormAsync();
    var updatedUser = await userService.ChangePasswordAsync(
        context.User,
        form["newPassword"],
        form["confirmPassword"]);

    if (updatedUser is null)
    {
        return Results.Redirect("/change-password?error=true");
    }

    var claims = new[]
    {
        new Claim("UserId", updatedUser.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Claim("UserName", updatedUser.UserName),
        new Claim(ClaimTypes.Role, updatedUser.Role.ToString()),
        new Claim("MustChangePassword", updatedUser.MustChangePassword.ToString())
    };
    var identity = new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme,
        "UserName",
        ClaimTypes.Role);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity));

    return Results.Redirect("/");
}).RequireAuthorization();

app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await IsValidAntiforgeryRequestAsync(context, antiforgery))
    {
        return Results.BadRequest();
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login?loggedOut=true");
}).RequireAuthorization();

app.MapPost("/account/setup", async (
    HttpContext context,
    IAntiforgery antiforgery,
    UserService userService) =>
{
    if (!await IsValidAntiforgeryRequestAsync(context, antiforgery))
    {
        return Results.BadRequest();
    }

    var form = await context.Request.ReadFormAsync();
    var created = await userService.CreateInitialAdministratorAsync(
        form["fullName"],
        form["userName"],
        form["password"]);

    return created
        ? Results.Redirect("/login?setup=complete")
        : Results.Redirect("/setup?error=1");
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task<bool> IsValidAntiforgeryRequestAsync(HttpContext context, IAntiforgery antiforgery)
{
    if (!context.Request.HasFormContentType)
    {
        return false;
    }

    try
    {
        await antiforgery.ValidateRequestAsync(context);
        return true;
    }
    catch (AntiforgeryValidationException)
    {
        return false;
    }
}