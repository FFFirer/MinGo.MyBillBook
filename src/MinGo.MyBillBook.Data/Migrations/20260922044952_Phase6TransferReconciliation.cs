using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6TransferReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "NormalizedTransactions",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BalanceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BankBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    CalculatedBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    Difference = table.Column<decimal>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BalanceSnapshots_FundAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "FundAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    ToAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchedRecordIds = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transfers_FundAccounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalTable: "FundAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transfers_FundAccounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "FundAccounts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotId = table.Column<int>(type: "INTEGER", nullable: true),
                    Difference = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationIssues_BalanceSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "BalanceSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReconciliationIssues_FundAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "FundAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceSnapshots_AccountId_SnapshotDate",
                table: "BalanceSnapshots",
                columns: new[] { "AccountId", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationIssues_AccountId",
                table: "ReconciliationIssues",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationIssues_SnapshotId",
                table: "ReconciliationIssues",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationIssues_Status",
                table: "ReconciliationIssues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_FromAccountId",
                table: "Transfers",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_OccurredAt",
                table: "Transfers",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_Status",
                table: "Transfers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ToAccountId",
                table: "Transfers",
                column: "ToAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationIssues");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropTable(
                name: "BalanceSnapshots");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "NormalizedTransactions");
        }
    }
}
