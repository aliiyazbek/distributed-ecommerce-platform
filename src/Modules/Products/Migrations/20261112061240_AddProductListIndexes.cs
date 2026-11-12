using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Products.Migrations
{
    /// <inheritdoc />
    public partial class AddProductListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_CreatedAt_Id",
                schema: "products",
                table: "Products",
                columns: new[] { "CreatedAt", "Id" });

            // Category filter + ordering. The query compares lower("Category") (EF translates
            // Category.ToLower()), so the index must be on that expression; EF Core's HasIndex
            // cannot declare expression indexes, hence raw SQL.
            migrationBuilder.Sql(
                """CREATE INDEX "IX_Products_LowerCategory_CreatedAt_Id" ON products."Products" (lower("Category"), "CreatedAt", "Id");""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS products."IX_Products_LowerCategory_CreatedAt_Id";""");

            migrationBuilder.DropIndex(
                name: "IX_Products_CreatedAt_Id",
                schema: "products",
                table: "Products");
        }
    }
}
