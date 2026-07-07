using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Klods.Migrations
{
    /// <inheritdoc />
    public partial class AddSetUpdateNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SetUpdateNotifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SetId = table.Column<string>(type: "text", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetUpdateNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetUpdateNotifications_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "SetId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetUpdateNotifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SetUpdateNotificationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NotificationId = table.Column<int>(type: "integer", nullable: false),
                    PartNum = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    ChangeKind = table.Column<string>(type: "text", nullable: false),
                    OldCount = table.Column<int>(type: "integer", nullable: false),
                    NewCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetUpdateNotificationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetUpdateNotificationItems_SetUpdateNotifications_Notificat~",
                        column: x => x.NotificationId,
                        principalTable: "SetUpdateNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SetUpdateNotificationItems_NotificationId",
                table: "SetUpdateNotificationItems",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_SetUpdateNotifications_SetId",
                table: "SetUpdateNotifications",
                column: "SetId");

            migrationBuilder.CreateIndex(
                name: "IX_SetUpdateNotifications_UserId_ReadAt",
                table: "SetUpdateNotifications",
                columns: new[] { "UserId", "ReadAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SetUpdateNotificationItems");

            migrationBuilder.DropTable(
                name: "SetUpdateNotifications");
        }
    }
}
