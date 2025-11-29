using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddSetIndexAsPK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SetMinifig",
                table: "SetMinifig");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetBricks",
                table: "SetBricks");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetMinifig",
                table: "SetMinifig",
                columns: new[] { "MinifigId", "SetId", "SetIndex" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetBricks",
                table: "SetBricks",
                columns: new[] { "PartNum", "ColorId", "SetId", "SetIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SetMinifig",
                table: "SetMinifig");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetBricks",
                table: "SetBricks");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetMinifig",
                table: "SetMinifig",
                columns: new[] { "MinifigId", "SetId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetBricks",
                table: "SetBricks",
                columns: new[] { "PartNum", "ColorId", "SetId" });
        }
    }
}
