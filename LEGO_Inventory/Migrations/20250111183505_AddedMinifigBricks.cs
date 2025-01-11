using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddedMinifigBricks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinifigCount",
                table: "Sets");

            migrationBuilder.CreateTable(
                name: "MinifigBricks",
                columns: table => new
                {
                    MinifigID = table.Column<string>(type: "TEXT", nullable: false),
                    BrickID = table.Column<string>(type: "TEXT", nullable: false),
                    ColorId = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinifigBricks", x => new { x.MinifigID, x.BrickID, x.ColorId });
                    table.ForeignKey(
                        name: "FK_MinifigBricks_Bricks_BrickID_ColorId",
                        columns: x => new { x.BrickID, x.ColorId },
                        principalTable: "Bricks",
                        principalColumns: new[] { "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MinifigBricks_Minifigs_MinifigID",
                        column: x => x.MinifigID,
                        principalTable: "Minifigs",
                        principalColumn: "MinifigId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MinifigBricks_BrickID_ColorId",
                table: "MinifigBricks",
                columns: new[] { "BrickID", "ColorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MinifigBricks");

            migrationBuilder.AddColumn<int>(
                name: "MinifigCount",
                table: "Sets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }
    }
}
