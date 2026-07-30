using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RenoTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedInspectorId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Inspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InspectorId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Inspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Inspections_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Angebote",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    InspectionId = table.Column<int>(type: "int", nullable: true),
                    AngebotNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedByInspectorId = table.Column<int>(type: "int", nullable: false),
                    ReviewedByAdminId = table.Column<int>(type: "int", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NetTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GrossTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Angebote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Angebote_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Angebote_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InspectionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionPhotos_Inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "Inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AngebotReviewComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AngebotId = table.Column<int>(type: "int", nullable: false),
                    AdminUserId = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AngebotReviewComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AngebotReviewComments_Angebote_AngebotId",
                        column: x => x.AngebotId,
                        principalTable: "Angebote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AngebotSections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    AngebotId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AngebotSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AngebotSections_Angebote_AngebotId",
                        column: x => x.AngebotId,
                        principalTable: "Angebote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AngebotItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CatalogItemId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Specification = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatRate = table.Column<int>(type: "int", nullable: false),
                    SectionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AngebotItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AngebotItems_AngebotSections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "AngebotSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DefaultSpecification = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DefaultUnit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SuggestedUnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedFromAngebotItemId = table.Column<int>(type: "int", nullable: true),
                    IsRetired = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogItems_AngebotItems_CreatedFromAngebotItemId",
                        column: x => x.CreatedFromAngebotItemId,
                        principalTable: "AngebotItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Angebote_AngebotNumber",
                table: "Angebote",
                column: "AngebotNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Angebote_InspectionId",
                table: "Angebote",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Angebote_LeadId",
                table: "Angebote",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Angebote_Status",
                table: "Angebote",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AngebotItems_CatalogItemId",
                table: "AngebotItems",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_AngebotItems_SectionId",
                table: "AngebotItems",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AngebotReviewComments_AngebotId",
                table: "AngebotReviewComments",
                column: "AngebotId");

            migrationBuilder.CreateIndex(
                name: "IX_AngebotSections_AngebotId",
                table: "AngebotSections",
                column: "AngebotId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_CreatedFromAngebotItemId",
                table: "CatalogItems",
                column: "CreatedFromAngebotItemId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionPhotos_InspectionId",
                table: "InspectionPhotos",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Inspections_LeadId",
                table: "Inspections",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_Status_AssignedInspectorId",
                table: "Leads",
                columns: new[] { "Status", "AssignedInspectorId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AngebotItems_CatalogItems_CatalogItemId",
                table: "AngebotItems",
                column: "CatalogItemId",
                principalTable: "CatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Angebote_Inspections_InspectionId",
                table: "Angebote");

            migrationBuilder.DropForeignKey(
                name: "FK_Angebote_Leads_LeadId",
                table: "Angebote");

            migrationBuilder.DropForeignKey(
                name: "FK_AngebotItems_AngebotSections_SectionId",
                table: "AngebotItems");

            migrationBuilder.DropForeignKey(
                name: "FK_AngebotItems_CatalogItems_CatalogItemId",
                table: "AngebotItems");

            migrationBuilder.DropTable(
                name: "AngebotReviewComments");

            migrationBuilder.DropTable(
                name: "InspectionPhotos");

            migrationBuilder.DropTable(
                name: "Inspections");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "AngebotSections");

            migrationBuilder.DropTable(
                name: "Angebote");

            migrationBuilder.DropTable(
                name: "CatalogItems");

            migrationBuilder.DropTable(
                name: "AngebotItems");
        }
    }
}
