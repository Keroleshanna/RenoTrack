using Microsoft.EntityFrameworkCore;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Identity;

/// <inheritdoc />
/// <remarks>
/// Reads Identity's tables through <see cref="RenoTrackDbContext"/> rather than <c>UserManager</c>,
/// matching <see cref="UserQueries"/> and <see cref="TokenService"/>: this is a read-only projection,
/// and going through UserManager would materialise full user objects and issue a role query per user
/// to answer what one join answers.
/// </remarks>
public sealed class UserDirectoryQueries(RenoTrackDbContext dbContext) : IUserDirectoryQueries
{
    public async Task<IReadOnlyList<UserSummaryDto>> GetStaffAsync(
        string? role,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        var query = from user in dbContext.Users.AsNoTracking()
                    join userRole in dbContext.UserRoles on user.Id equals userRole.UserId
                    join identityRole in dbContext.Roles on userRole.RoleId equals identityRole.Id
                    select new { user.Id, user.Name, user.IsActive, RoleName = identityRole.Name! };

        if (!string.IsNullOrWhiteSpace(role))
        {
            query = query.Where(row => row.RoleName == role);
        }

        if (activeOnly)
        {
            query = query.Where(row => row.IsActive);
        }

        return await query
            // By name, because this list is read by a human choosing a colleague from it. Id breaks
            // ties so the order is stable between requests — two people can share a display name.
            .OrderBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Select(row => new UserSummaryDto(row.Id, row.Name, row.RoleName, row.IsActive))
            .ToListAsync(cancellationToken);
    }
}
