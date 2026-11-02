using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SRT.Core.Database.SQLServer.ErrorLog.Migrations
{
    /// <inheritdoc />
    public partial class AddErrorReportViewerIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ErrorReports_date",
                table: "ErrorReports",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorReports_Operation",
                table: "ErrorReports",
                column: "Operation");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorReports_Severity_date",
                table: "ErrorReports",
                columns: new[] { "Severity", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ErrorReports_date",
                table: "ErrorReports");

            migrationBuilder.DropIndex(
                name: "IX_ErrorReports_Operation",
                table: "ErrorReports");

            migrationBuilder.DropIndex(
                name: "IX_ErrorReports_Severity_date",
                table: "ErrorReports");
        }
    }
}
