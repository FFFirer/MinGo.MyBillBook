using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinGo.MyBillBook.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourcePaymentTransactionIdToBillRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourcePaymentTransactionId",
                table: "BillRecords",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_BillRecords_SourcePaymentTransactionId",
                table: "BillRecords",
                column: "SourcePaymentTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillRecords_SourcePaymentTransactionId",
                table: "BillRecords");

            migrationBuilder.DropColumn(
                name: "SourcePaymentTransactionId",
                table: "BillRecords");
        }
    }
}
