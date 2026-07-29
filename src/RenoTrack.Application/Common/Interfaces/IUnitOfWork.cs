namespace RenoTrack.Application.Common.Interfaces;

/// <summary>
/// The single commit boundary for a use case (Sequence Diagram §4: "SaveChangesAsync (via
/// IUnitOfWork)"). Repository <c>AddAsync</c> calls only stage changes; nothing reaches the
/// database until a handler calls this.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
