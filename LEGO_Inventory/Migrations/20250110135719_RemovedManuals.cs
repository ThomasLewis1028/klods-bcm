using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class RemovedManuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManualPath",
                table: "Sets");

            migrationBuilder.DropColumn(
                name: "ManualURL",
                table: "Sets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManualPath",
                table: "Sets",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ManualURL",
                table: "Sets",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
