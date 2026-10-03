using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRT.Core.Database.SQLServer.Cache.Migrations
{
    /// <inheritdoc />
    public partial class InitialCashBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashBackup",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    category = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    serviceID = table.Column<int>(type: "int", nullable: false),
                    key = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    value1 = table.Column<string>(type: "nvarchar(3990)", maxLength: 3990, nullable: true),
                    value2 = table.Column<string>(type: "nvarchar(3990)", maxLength: 3990, nullable: true),
                    value3 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dateCreate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    dateExpire = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashBackup", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashBackup_key",
                table: "CashBackup",
                column: "key");

            migrationBuilder.CreateIndex(
                name: "IX_CashBackup_serviceID",
                table: "CashBackup",
                column: "serviceID");

            migrationBuilder.CreateIndex(
                name: "UX_CashBackup_serviceID_category_key",
                table: "CashBackup",
                columns: new[] { "serviceID", "category", "key" },
                unique: true,
                filter: "[category] IS NOT NULL AND [key] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashBackup");
        }
    }
}
