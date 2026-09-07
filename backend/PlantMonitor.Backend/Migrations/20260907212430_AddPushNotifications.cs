using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlantMonitor.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notified_status",
                table: "plants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    p256dh = table.Column<string>(type: "text", nullable: false),
                    auth = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropColumn(
                name: "notified_status",
                table: "plants");
        }
    }
}
