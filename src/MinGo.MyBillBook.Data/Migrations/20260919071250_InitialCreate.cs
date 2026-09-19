using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillCategories_BillCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "BillCategories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PaymentPlatforms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IconUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPlatforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchField = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchPattern = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryRules_BillCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "BillCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlatformId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ImportDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillImportBatches_PaymentPlatforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "PaymentPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FundAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PlatformId = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountType = table.Column<int>(type: "INTEGER", nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FundAccounts_PaymentPlatforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "PaymentPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillRawRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawData = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: false),
                    Counterparty = table.Column<string>(type: "TEXT", nullable: false),
                    ProductName = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TransactionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsProcessed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillRawRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillRawRecords_BillImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "BillImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillRawRecords_PaymentPlatforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "PaymentPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RawRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformId = table.Column<int>(type: "INTEGER", nullable: false),
                    FundAccountId = table.Column<int>(type: "INTEGER", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Counterparty = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Merchant = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    TransactionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SourceFile = table.Column<string>(type: "TEXT", nullable: false),
                    IsManualAdjusted = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyncedToDuckDb = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillRecords_BillCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "BillCategories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BillRecords_BillRawRecords_RawRecordId",
                        column: x => x.RawRecordId,
                        principalTable: "BillRawRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillRecords_FundAccounts_FundAccountId",
                        column: x => x.FundAccountId,
                        principalTable: "FundAccounts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BillRecords_PaymentPlatforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "PaymentPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillCategories_ParentId",
                table: "BillCategories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_BillImportBatches_PlatformId",
                table: "BillImportBatches",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRawRecords_ImportBatchId",
                table: "BillRawRecords",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRawRecords_PlatformId",
                table: "BillRawRecords",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRawRecords_TransactionId",
                table: "BillRawRecords",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_CategoryId",
                table: "BillRecords",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_FundAccountId",
                table: "BillRecords",
                column: "FundAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_PlatformId",
                table: "BillRecords",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_RawRecordId",
                table: "BillRecords",
                column: "RawRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_SyncedToDuckDb",
                table: "BillRecords",
                column: "SyncedToDuckDb");

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_TransactionDate",
                table: "BillRecords",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRules_CategoryId",
                table: "CategoryRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRules_Priority",
                table: "CategoryRules",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_FundAccounts_PlatformId",
                table: "FundAccounts",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlatforms_Code",
                table: "PaymentPlatforms",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillRecords");

            migrationBuilder.DropTable(
                name: "CategoryRules");

            migrationBuilder.DropTable(
                name: "BillRawRecords");

            migrationBuilder.DropTable(
                name: "FundAccounts");

            migrationBuilder.DropTable(
                name: "BillCategories");

            migrationBuilder.DropTable(
                name: "BillImportBatches");

            migrationBuilder.DropTable(
                name: "PaymentPlatforms");
        }
    }
}
