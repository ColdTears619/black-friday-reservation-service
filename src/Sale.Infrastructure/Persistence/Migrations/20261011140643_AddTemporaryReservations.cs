using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sale.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Reservations_Expiry", "[ExpiresAt] > [CreatedAt]");
                    table.CheckConstraint("CK_Reservations_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_Reservations_Status", "([Status] IN ('Pending', 'Expired') AND [ConfirmedAt] IS NULL) OR ([Status] = 'Confirmed' AND [ConfirmedAt] >= [CreatedAt] AND [ConfirmedAt] < [ExpiresAt] AND [ConfirmedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_Reservations_UnitPrice", "[UnitPrice] > 0");
                    table.ForeignKey(
                        name: "FK_Reservations_SaleEventItems_TenantId_SaleEventId_Sku",
                        columns: x => new { x.TenantId, x.SaleEventId, x.Sku },
                        principalTable: "SaleEventItems",
                        principalColumns: new[] { "TenantId", "SaleEventId", "Sku" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessingJobs",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessingJobs", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ProcessingJobs_Reservations_TenantId_ReservationId",
                        columns: x => new { x.TenantId, x.ReservationId },
                        principalTable: "Reservations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_TenantId_ReservationId",
                table: "ProcessingJobs",
                columns: new[] { "TenantId", "ReservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_ExpiresAt",
                table: "Reservations",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TenantId_IdempotencyKey",
                table: "Reservations",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TenantId_SaleEventId_Sku",
                table: "Reservations",
                columns: new[] { "TenantId", "SaleEventId", "Sku" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessingJobs");

            migrationBuilder.DropTable(
                name: "Reservations");
        }
    }
}
