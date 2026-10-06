using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemiPos.Infrastructure.Persistence.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddInventoryExpiryLotsAndMovementLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BaseQuantityPerPackage",
                table: "PurchaseItems",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUnitCode",
                table: "PurchaseItems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductPresentationId",
                table: "PurchaseItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PreviousCost",
                table: "Products",
                type: "numeric(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LastCost",
                table: "Products",
                type: "numeric(18,6)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ExpiryDate",
                table: "InventoryExpiryLots",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<bool>(
                name: "IsOpeningBalance",
                table: "InventoryExpiryLots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LotNumber",
                table: "InventoryExpiryLots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReceivedAtUtc",
                table: "InventoryExpiryLots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQuantity",
                table: "InventoryExpiryLots",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                table: "InventoryExpiryLots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "InventoryExpiryLots",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "InventoryMovementLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ReversesAllocationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovementLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryMovementLots_InventoryExpiryLots_LotId",
                        column: x => x.LotId,
                        principalTable: "InventoryExpiryLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovementLots_InventoryMovementLots_ReversesAllocat~",
                        column: x => x.ReversesAllocationId,
                        principalTable: "InventoryMovementLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovementLots_InventoryMovements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryExpiryLots_WarehouseId",
                table: "InventoryExpiryLots",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryExpiryLots_Warehouses_WarehouseId",
                table: "InventoryExpiryLots",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovementLots_LotId",
                table: "InventoryMovementLots",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovementLots_MovementId_LotId_ReversesAllocationId",
                table: "InventoryMovementLots",
                columns: new[] { "MovementId", "LotId", "ReversesAllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovementLots_ReversesAllocationId",
                table: "InventoryMovementLots",
                column: "ReversesAllocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryMovementLots");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryExpiryLots_Warehouses_WarehouseId",
                table: "InventoryExpiryLots");

            migrationBuilder.DropIndex(
                name: "IX_InventoryExpiryLots_WarehouseId",
                table: "InventoryExpiryLots");

            migrationBuilder.DropColumn(
                name: "BaseQuantityPerPackage",
                table: "PurchaseItems");

            migrationBuilder.DropColumn(
                name: "BaseUnitCode",
                table: "PurchaseItems");

            migrationBuilder.DropColumn(
                name: "ProductPresentationId",
                table: "PurchaseItems");

            migrationBuilder.AlterColumn<decimal>(
                name: "PreviousCost",
                table: "Products",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)");

            migrationBuilder.AlterColumn<decimal>(
                name: "LastCost",
                table: "Products",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "ExpiryDate",
                table: "InventoryExpiryLots",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "IsOpeningBalance",
                table: "InventoryExpiryLots");

            migrationBuilder.DropColumn(
                name: "LotNumber",
                table: "InventoryExpiryLots");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUtc",
                table: "InventoryExpiryLots");

            migrationBuilder.DropColumn(
                name: "ReceivedQuantity",
                table: "InventoryExpiryLots");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "InventoryExpiryLots");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "InventoryExpiryLots");
        }
    }
}
