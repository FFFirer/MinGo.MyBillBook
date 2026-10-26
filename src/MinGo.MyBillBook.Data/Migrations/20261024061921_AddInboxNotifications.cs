using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RunId = table.Column<int>(type: "INTEGER", nullable: true),
                    TodoId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkflowName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxNotifications_CreatedAt",
                table: "InboxNotifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InboxNotifications_IsRead",
                table: "InboxNotifications",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_InboxNotifications_RunId",
                table: "InboxNotifications",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxNotifications");
        }
    }
}
