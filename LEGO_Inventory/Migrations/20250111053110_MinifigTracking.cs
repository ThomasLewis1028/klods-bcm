using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class MinifigTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NumParts",
                table: "Sets",
                newName: "NumBricks");

            migrationBuilder.AddColumn<int>(
                name: "MinifigCount",
                table: "Sets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MinifigId",
                table: "Bricks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Minifigs",
                columns: table => new
                {
                    MinifigId = table.Column<string>(type: "TEXT", nullable: false),
                    MinifigName = table.Column<string>(type: "TEXT", nullable: false),
                    MinifigImgUrl = table.Column<string>(type: "TEXT", nullable: false),
                    MinifigUrl = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Minifigs", x => x.MinifigId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bricks_MinifigId",
                table: "Bricks",
                column: "MinifigId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bricks_Minifigs_MinifigId",
                table: "Bricks",
                column: "MinifigId",
                principalTable: "Minifigs",
                principalColumn: "MinifigId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bricks_Minifigs_MinifigId",
                table: "Bricks");

            migrationBuilder.DropTable(
                name: "Minifigs");

            migrationBuilder.DropIndex(
                name: "IX_Bricks_MinifigId",
                table: "Bricks");

            migrationBuilder.DropColumn(
                name: "MinifigCount",
                table: "Sets");

            migrationBuilder.DropColumn(
                name: "MinifigId",
                table: "Bricks");

            migrationBuilder.RenameColumn(
                name: "NumBricks",
                table: "Sets",
                newName: "NumParts");
        }
    }
}
