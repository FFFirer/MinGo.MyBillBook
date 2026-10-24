using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxAuditAndSensitiveFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RunId = table.Column<int>(type: "INTEGER", nullable: true),
                    TodoId = table.Column<int>(type: "INTEGER", nullable: true),
                    WorkflowName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxAuditEvents_EventType",
                table: "InboxAuditEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_InboxAuditEvents_OccurredAt",
                table: "InboxAuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_InboxAuditEvents_RunId",
                table: "InboxAuditEvents",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxAuditEvents");
        }
    }
}
