using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RenoTrack.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records that <c>TokenLinks.UsedAt</c> became an optimistic-concurrency token (D96).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>Up</c> and <c>Down</c> are empty, and that is the correct product of the model — not
    /// an unfinished migration.</b> For a non-<c>rowversion</c> column, EF Core's concurrency token
    /// is client-side behaviour only: it adds the column to the <c>WHERE</c> clause of the
    /// <c>UPDATE</c> statement it generates. The column itself already exists (migration #6
    /// <c>AddTokenLinks</c>) and its type, nullability and name are all unchanged, so there is no
    /// DDL to emit in either direction.
    /// </para>
    /// <para>
    /// The migration exists anyway because <c>RenoTrackDbContextModelSnapshot</c> <i>does</i> record
    /// <c>.IsConcurrencyToken()</c>. Without this file the snapshot and the model would disagree and
    /// <c>dotnet ef migrations has-pending-model-changes</c> would report a pending change forever —
    /// which CLAUDE.md §14 requires to be clean before a slice is complete. Its whole job is to move
    /// the snapshot forward.
    /// </para>
    /// <para>
    /// <b>Consequence for deployment (Architecture.md §13.1):</b> this migration applies instantly
    /// and takes no lock, but it must still be applied, because startup verification compares
    /// migration <i>history</i> in both directions and would otherwise refuse to serve.
    /// </para>
    /// </remarks>
    public partial class AddTokenLinkConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
