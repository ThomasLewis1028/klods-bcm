using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klods.Migrations
{
    /// <inheritdoc />
    public partial class AddBrickSetCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SetCount",
                table: "Bricks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // One-time backfill so the existing catalog gets popularity counts without a re-import.
            migrationBuilder.Sql("""
                UPDATE "Bricks" b SET "SetCount" = sub.cnt
                FROM (SELECT "PartNum", "ColorId", COUNT(*) AS cnt FROM "SetBricks" GROUP BY "PartNum", "ColorId") sub
                WHERE b."PartNum" = sub."PartNum" AND b."ColorId" = sub."ColorId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetCount",
                table: "Bricks");
        }
    }
}
