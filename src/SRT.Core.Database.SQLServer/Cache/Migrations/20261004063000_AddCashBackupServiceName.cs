using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRT.Core.Database.SQLServer.Cache.Migrations
{
    /// <inheritdoc />
    public partial class AddCashBackupServiceName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "serviceName",
                table: "CashBackup",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashBackup_serviceName",
                table: "CashBackup",
                column: "serviceName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashBackup_serviceName",
                table: "CashBackup");

            migrationBuilder.DropColumn(
                name: "serviceName",
                table: "CashBackup");
        }
    }
}
