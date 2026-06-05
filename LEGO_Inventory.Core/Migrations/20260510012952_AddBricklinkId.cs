using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddBricklinkId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BricklinkId",
                table: "Bricks",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BricklinkId",
                table: "Bricks");
        }
    }
}
