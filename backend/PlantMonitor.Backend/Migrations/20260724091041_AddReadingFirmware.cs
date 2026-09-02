using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantMonitor.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingFirmware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fw",
                table: "readings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fw",
                table: "readings");
        }
    }
}
