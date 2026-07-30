using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Infrastructure.Identity;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Only the columns ApplicationUser adds beyond IdentityUser&lt;int&gt;'s own base shape
/// (Email, PasswordHash, lockout fields, etc., all configured by IdentityDbContext's own
/// OnModelCreating, called before this). Table name/every other column stay the framework
/// default (AspNetUsers) — no reason to rename away from what every Identity tool/guide expects.
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.Name).IsRequired().HasMaxLength(200);
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();
    }
}
