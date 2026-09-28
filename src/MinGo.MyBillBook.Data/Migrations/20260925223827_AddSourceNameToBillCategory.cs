using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceNameToBillCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "BillCategories",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillCategories_SourceName",
                table: "BillCategories",
                column: "SourceName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillCategories_SourceName",
                table: "BillCategories");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "BillCategories");
        }
    }
}
