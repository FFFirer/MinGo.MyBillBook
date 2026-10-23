using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkflowName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WorkflowYaml = table.Column<string>(type: "TEXT", nullable: false),
                    TriggerType = table.Column<int>(type: "INTEGER", nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InputsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InboxStepRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<int>(type: "INTEGER", nullable: false),
                    JobName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    InputsJson = table.Column<string>(type: "TEXT", nullable: false),
                    OutputsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Log = table.Column<string>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxStepRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxStepRuns_InboxRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "InboxRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InboxTodos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<int>(type: "INTEGER", nullable: false),
                    StepRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    InputType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Placeholder = table.Column<string>(type: "TEXT", nullable: true),
                    IsSensitive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    InputValue = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxTodos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxTodos_InboxRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "InboxRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxRuns_CreatedAt",
                table: "InboxRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InboxRuns_Status",
                table: "InboxRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InboxRuns_WorkflowName",
                table: "InboxRuns",
                column: "WorkflowName");

            migrationBuilder.CreateIndex(
                name: "IX_InboxStepRuns_RunId",
                table: "InboxStepRuns",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxStepRuns_RunId_StepId",
                table: "InboxStepRuns",
                columns: new[] { "RunId", "StepId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxStepRuns_Status",
                table: "InboxStepRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InboxTodos_CreatedAt",
                table: "InboxTodos",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InboxTodos_RunId_StepRunId",
                table: "InboxTodos",
                columns: new[] { "RunId", "StepRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxTodos_Status",
                table: "InboxTodos",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxStepRuns");

            migrationBuilder.DropTable(
                name: "InboxTodos");

            migrationBuilder.DropTable(
                name: "InboxRuns");
        }
    }
}
