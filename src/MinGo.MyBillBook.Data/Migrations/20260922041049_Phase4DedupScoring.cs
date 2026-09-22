using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4DedupScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DuplicateScore",
                table: "NormalizedTransactions",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateStatus",
                table: "NormalizedTransactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "DuplicateScore",
                table: "BillRawRecords",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "DuplicateStatus",
                table: "BillRawRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DuplicateCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LeftRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    RightRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    MatchReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DuplicateCandidates_BillRecords_LeftRecordId",
                        column: x => x.LeftRecordId,
                        principalTable: "BillRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DuplicateCandidates_BillRecords_RightRecordId",
                        column: x => x.RightRecordId,
                        principalTable: "BillRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_LeftRecordId",
                table: "DuplicateCandidates",
                column: "LeftRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_RightRecordId",
                table: "DuplicateCandidates",
                column: "RightRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateCandidates_Status",
                table: "DuplicateCandidates",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuplicateCandidates");

            migrationBuilder.DropColumn(
                name: "DuplicateScore",
                table: "NormalizedTransactions");

            migrationBuilder.DropColumn(
                name: "DuplicateStatus",
                table: "NormalizedTransactions");

            migrationBuilder.DropColumn(
                name: "DuplicateScore",
                table: "BillRawRecords");

            migrationBuilder.DropColumn(
                name: "DuplicateStatus",
                table: "BillRawRecords");
        }
    }
}
