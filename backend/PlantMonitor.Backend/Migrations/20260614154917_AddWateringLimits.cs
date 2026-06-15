using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantMonitor.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddWateringLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "can_water_percent",
                table: "plants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "must_water_percent",
                table: "plants",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "can_water_percent",
                table: "plants");

            migrationBuilder.DropColumn(
                name: "must_water_percent",
                table: "plants");
        }
    }
}
