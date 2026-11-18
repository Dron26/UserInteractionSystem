using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserInteractionSystem.Domain.Entities;

namespace UserInteractionSystem.Infrastructure.Configurations;

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.FullName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(c => c.Phone)
            .HasMaxLength(30);

        builder.Property(c => c.Email)
            .HasMaxLength(254);

        builder.Property(c => c.Comment)
            .HasMaxLength(1000);
    }
}