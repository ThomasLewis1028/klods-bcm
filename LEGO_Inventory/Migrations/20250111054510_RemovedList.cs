using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class RemovedList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bricks_Minifigs_MinifigId",
                table: "Bricks");

            migrationBuilder.DropIndex(
                name: "IX_Bricks_MinifigId",
                table: "Bricks");

            migrationBuilder.DropColumn(
                name: "MinifigId",
                table: "Bricks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MinifigId",
                table: "Bricks",
                type: "TEXT",
                nullable: true);

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
    }
}
