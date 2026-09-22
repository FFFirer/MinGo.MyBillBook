using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2ModelUpgrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "BillRecords");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "BillRawRecords");

            migrationBuilder.DropColumn(
                name: "Counterparty",
                table: "BillRawRecords");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "BillRawRecords");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "BillRawRecords");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "BillRawRecords");

            migrationBuilder.DropColumn(
                name: "RawData",
                table: "BillRawRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "BillRawRecords");

            migrationBuilder.RenameColumn(
                name: "TransactionId",
                table: "BillRawRecords",
                newName: "SourceTransactionId");

            migrationBuilder.RenameColumn(
                name: "TransactionDate",
                table: "BillRawRecords",
                newName: "RawPayload");

            migrationBuilder.RenameIndex(
                name: "IX_BillRawRecords_TransactionId",
                table: "BillRawRecords",
                newName: "IX_BillRawRecords_SourceTransactionId");

            migrationBuilder.AddColumn<long>(
                name: "AmountMinor",
                table: "BillRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "RowNumber",
                table: "BillRawRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ClassificationResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TransactionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: true),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassificationResults_BillRecords_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "BillRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RawRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RawDescription = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Counterparty = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceTransactionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedTransactions_BillRawRecords_RawRecordId",
                        column: x => x.RawRecordId,
                        principalTable: "BillRawRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationResults_TransactionId",
                table: "ClassificationResults",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationResults_TransactionId_Field",
                table: "ClassificationResults",
                columns: new[] { "TransactionId", "Field" });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedTransactions_OccurredAt",
                table: "NormalizedTransactions",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedTransactions_RawRecordId",
                table: "NormalizedTransactions",
                column: "RawRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassificationResults");

            migrationBuilder.DropTable(
                name: "NormalizedTransactions");

            migrationBuilder.DropColumn(
                name: "AmountMinor",
                table: "BillRecords");

            migrationBuilder.DropColumn(
                name: "RowNumber",
                table: "BillRawRecords");

            migrationBuilder.RenameColumn(
                name: "SourceTransactionId",
                table: "BillRawRecords",
                newName: "TransactionId");

            migrationBuilder.RenameColumn(
                name: "RawPayload",
                table: "BillRawRecords",
                newName: "TransactionDate");

            migrationBuilder.RenameIndex(
                name: "IX_BillRawRecords_SourceTransactionId",
                table: "BillRawRecords",
                newName: "IX_BillRawRecords_TransactionId");

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "BillRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "BillRawRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Counterparty",
                table: "BillRawRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "BillRawRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "BillRawRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "BillRawRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawData",
                table: "BillRawRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "BillRawRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
