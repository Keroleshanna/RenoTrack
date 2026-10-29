using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RenoTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAngebotVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Angebote",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Angebote");
        }
    }
}
