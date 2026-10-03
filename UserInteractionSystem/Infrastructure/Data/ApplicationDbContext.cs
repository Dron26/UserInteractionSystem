using Microsoft.EntityFrameworkCore;
using UserInteractionSystem.Domain.Entities;

namespace UserInteractionSystem.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
    public DbSet<Client> Clients { get; set; } = null!;

    public DbSet<User> Users { get; set; } = null!;

    public DbSet<Request> Requests { get; set; } = null!;

    public DbSet<Interaction> Interactions { get; set; } = null!;
}
