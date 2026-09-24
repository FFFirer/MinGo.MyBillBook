using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTransactionIdAndSourceTransactionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourcePaymentTransactionId",
                table: "NormalizedTransactions",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceTransactionId",
                table: "BillRecords",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourcePaymentTransactionId",
                table: "BillRawRecords",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_SourceTransactionId",
                table: "BillRecords",
                column: "SourceTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillRawRecords_SourcePaymentTransactionId",
                table: "BillRawRecords",
                column: "SourcePaymentTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillRecords_SourceTransactionId",
                table: "BillRecords");

            migrationBuilder.DropIndex(
                name: "IX_BillRawRecords_SourcePaymentTransactionId",
                table: "BillRawRecords");

            migrationBuilder.DropColumn(
                name: "SourcePaymentTransactionId",
                table: "NormalizedTransactions");

            migrationBuilder.DropColumn(
                name: "SourceTransactionId",
                table: "BillRecords");

            migrationBuilder.DropColumn(
                name: "SourcePaymentTransactionId",
                table: "BillRawRecords");
        }
    }
}
