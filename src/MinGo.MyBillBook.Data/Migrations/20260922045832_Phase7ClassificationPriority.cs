using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7ClassificationPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultCategoryId",
                table: "Merchants",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "BillCategories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_DefaultCategoryId",
                table: "Merchants",
                column: "DefaultCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Merchants_BillCategories_DefaultCategoryId",
                table: "Merchants",
                column: "DefaultCategoryId",
                principalTable: "BillCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Merchants_BillCategories_DefaultCategoryId",
                table: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_Merchants_DefaultCategoryId",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "DefaultCategoryId",
                table: "Merchants");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "BillCategories");
        }
    }
}
