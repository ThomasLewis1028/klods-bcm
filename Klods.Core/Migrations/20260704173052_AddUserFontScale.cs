using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klods.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFontScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "FontScale",
                table: "Users",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FontScale",
                table: "Users");
        }
    }
}
