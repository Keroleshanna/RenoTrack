using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RenoTrack.Domain.Entities;

namespace RenoTrack.Infrastructure.Persistence.Configurations;

/// <summary>The InspectionId FK is a shadow property (configured from InspectionConfiguration) — InspectionPhoto has no visible C# FK property, matching its own doc comment.</summary>
public sealed class InspectionPhotoConfiguration : IEntityTypeConfiguration<InspectionPhoto>
{
    public void Configure(EntityTypeBuilder<InspectionPhoto> builder)
    {
        builder.ToTable("InspectionPhotos");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FileUrl).IsRequired().HasMaxLength(1000);
        builder.Property(p => p.Caption).HasMaxLength(500);
        builder.Property(p => p.UploadedAt).IsRequired();
    }
}
