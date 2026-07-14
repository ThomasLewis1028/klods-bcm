using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klods.Migrations
{
    /// <inheritdoc />
    public partial class AddBodyStyle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyStyle",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BodyStyle",
                table: "Users");
        }
    }
}
