using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Orders.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId",
                schema: "orders",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt_Id",
                schema: "orders",
                table: "Orders",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_CreatedAt_Id",
                schema: "orders",
                table: "Orders",
                columns: new[] { "UserId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt_Id",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_UserId_CreatedAt_Id",
                schema: "orders",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId",
                schema: "orders",
                table: "Orders",
                column: "UserId");
        }
    }
}
