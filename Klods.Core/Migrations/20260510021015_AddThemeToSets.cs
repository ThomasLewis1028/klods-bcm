using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klods.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeToSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ThemeId",
                table: "Sets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeName",
                table: "Sets",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThemeId",
                table: "Sets");

            migrationBuilder.DropColumn(
                name: "ThemeName",
                table: "Sets");
        }
    }
}
