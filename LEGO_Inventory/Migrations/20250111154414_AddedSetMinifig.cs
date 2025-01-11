using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddedSetMinifig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SetMinifig",
                columns: table => new
                {
                    MinifigId = table.Column<string>(type: "TEXT", nullable: false),
                    SetId = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetMinifig", x => new { x.MinifigId, x.SetId });
                    table.ForeignKey(
                        name: "FK_SetMinifig_Minifigs_MinifigId",
                        column: x => x.MinifigId,
                        principalTable: "Minifigs",
                        principalColumn: "MinifigId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetMinifig_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "SetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SetMinifig_SetId",
                table: "SetMinifig",
                column: "SetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SetMinifig");
        }
    }
}
