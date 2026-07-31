using Microsoft.EntityFrameworkCore;
using RenoTrack.Application.Common.Interfaces;
using RenoTrack.Infrastructure.Persistence;

namespace RenoTrack.Infrastructure.Identity;

/// <inheritdoc />
/// <remarks>
/// Queries Identity's own tables through <see cref="RenoTrackDbContext"/> rather than
/// <c>UserManager</c>, matching how <see cref="TokenService"/> already reads roles: this is a
/// read-only projection to a boolean, and going through UserManager would materialize a full user
/// and a role list to answer a question the database can answer with an existence check.
/// </remarks>
public sealed class UserQueries(RenoTrackDbContext dbContext) : IUserQueries
{
    public Task<bool> IsActiveInspectorAsync(int userId, CancellationToken cancellationToken) =>
        (from user in dbContext.Users
         join userRole in dbContext.UserRoles on user.Id equals userRole.UserId
         join role in dbContext.Roles on userRole.RoleId equals role.Id
         where user.Id == userId
             && user.IsActive
             && role.Name == IdentityRoleSeeder.InspectorRole
         select user.Id)
        .AnyAsync(cancellationToken);
}
