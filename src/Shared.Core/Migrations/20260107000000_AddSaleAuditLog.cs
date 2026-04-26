using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shared.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaleAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    EventDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OldValues = table.Column<string>(type: "TEXT", nullable: true),
                    NewValues = table.Column<string>(type: "TEXT", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleAuditLogs", x => x.Id);
                });

            // Indexes for efficient querying by sale, user, and timestamp (Requirements 10.1–10.3)
            migrationBuilder.CreateIndex(
                name: "IX_SaleAuditLog_SaleId",
                table: "SaleAuditLogs",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleAuditLog_UserId",
                table: "SaleAuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleAuditLog_Timestamp",
                table: "SaleAuditLogs",
                column: "Timestamp");

            // Composite indexes for common query patterns
            migrationBuilder.CreateIndex(
                name: "IX_SaleAuditLog_Sale_Timestamp",
                table: "SaleAuditLogs",
                columns: new[] { "SaleId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleAuditLog_User_Timestamp",
                table: "SaleAuditLogs",
                columns: new[] { "UserId", "Timestamp" });

            // Soft-delete index
            migrationBuilder.CreateIndex(
                name: "IX_SaleAuditLogs_IsDeleted",
                table: "SaleAuditLogs",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SaleAuditLogs");
        }
    }
}
