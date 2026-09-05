using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RenoTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAngebotDecisionReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                table: "Angebote",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecisionReason",
                table: "Angebote");
        }
    }
}
