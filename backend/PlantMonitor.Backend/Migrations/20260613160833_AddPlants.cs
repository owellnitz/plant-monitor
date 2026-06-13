using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantMonitor.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPlants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plant_species",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plant_species", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    species_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    sun_exposure = table.Column<string>(type: "text", nullable: true),
                    device_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plants", x => x.id);
                    table.ForeignKey(
                        name: "FK_plants_plant_species_species_id",
                        column: x => x.species_id,
                        principalTable: "plant_species",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_plant_species_name",
                table: "plant_species",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plants_device_id",
                table: "plants",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plants_species_id",
                table: "plants",
                column: "species_id");

            // Starter species list; users extend it via the plant form.
            migrationBuilder.Sql(
                """
                INSERT INTO plant_species (name) VALUES
                    ('Monstera'), ('Pothos'), ('Snake plant'), ('Peace lily'),
                    ('Basil'), ('Aloe'), ('Fern'), ('Succulent')
                ON CONFLICT (name) DO NOTHING
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plants");

            migrationBuilder.DropTable(
                name: "plant_species");
        }
    }
}
