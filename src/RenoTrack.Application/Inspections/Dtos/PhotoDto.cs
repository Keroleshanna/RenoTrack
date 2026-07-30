using RenoTrack.Domain.Entities;

namespace RenoTrack.Application.Inspections.Dtos;

public sealed record PhotoDto(int Id, string FileUrl, string? Caption, DateTime UploadedAt);

public static class PhotoMappingExtensions
{
    public static PhotoDto ToDto(this InspectionPhoto photo) => new(photo.Id, photo.FileUrl, photo.Caption, photo.UploadedAt);
}
