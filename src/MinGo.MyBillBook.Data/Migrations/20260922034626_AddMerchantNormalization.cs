using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MerchantId",
                table: "BillRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Merchants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CanonicalName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Merchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MerchantAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MatchType = table.Column<int>(type: "INTEGER", nullable: false),
                    MerchantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantAliases_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_MerchantId",
                table: "BillRecords",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_MerchantId",
                table: "MerchantAliases",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_Priority",
                table: "MerchantAliases",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_CanonicalName",
                table: "Merchants",
                column: "CanonicalName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BillRecords_Merchants_MerchantId",
                table: "BillRecords",
                column: "MerchantId",
                principalTable: "Merchants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BillRecords_Merchants_MerchantId",
                table: "BillRecords");

            migrationBuilder.DropTable(
                name: "MerchantAliases");

            migrationBuilder.DropTable(
                name: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_BillRecords_MerchantId",
                table: "BillRecords");

            migrationBuilder.DropColumn(
                name: "MerchantId",
                table: "BillRecords");
        }
    }
}
