using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantMonitor.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingResetAndOta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ota",
                table: "readings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reset",
                table: "readings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ota",
                table: "readings");

            migrationBuilder.DropColumn(
                name: "reset",
                table: "readings");
        }
    }
}
