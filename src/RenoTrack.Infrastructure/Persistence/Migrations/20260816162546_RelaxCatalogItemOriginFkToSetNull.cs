using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RenoTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelaxCatalogItemOriginFkToSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CatalogItems_AngebotItems_CreatedFromAngebotItemId",
                table: "CatalogItems");

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogItems_AngebotItems_CreatedFromAngebotItemId",
                table: "CatalogItems",
                column: "CreatedFromAngebotItemId",
                principalTable: "AngebotItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CatalogItems_AngebotItems_CreatedFromAngebotItemId",
                table: "CatalogItems");

            migrationBuilder.AddForeignKey(
                name: "FK_CatalogItems_AngebotItems_CreatedFromAngebotItemId",
                table: "CatalogItems",
                column: "CreatedFromAngebotItemId",
                principalTable: "AngebotItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
