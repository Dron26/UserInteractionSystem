using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserInteractionSystem.Domain.Entities;

namespace UserInteractionSystem.Infrastructure.Configurations;

public class InteractionConfiguration : IEntityTypeConfiguration<Interaction>
{
    public void Configure(EntityTypeBuilder<Interaction> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Content)
            .IsRequired();

        builder.HasOne<Request>()
            .WithMany()
            .HasForeignKey(i => i.RequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}