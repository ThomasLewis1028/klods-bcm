using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bricks",
                columns: table => new
                {
                    PartNum = table.Column<string>(type: "TEXT", nullable: false),
                    ColorId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PartURL = table.Column<string>(type: "TEXT", nullable: true),
                    PartImg = table.Column<string>(type: "TEXT", nullable: true),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    ColorName = table.Column<string>(type: "TEXT", nullable: true),
                    HexColor = table.Column<string>(type: "TEXT", nullable: true),
                    IsTrans = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bricks", x => new { x.PartNum, x.ColorId });
                });

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

            migrationBuilder.CreateTable(
                name: "Sets",
                columns: table => new
                {
                    SetId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SetURL = table.Column<string>(type: "TEXT", nullable: false),
                    SetImg = table.Column<string>(type: "TEXT", nullable: false),
                    NumBricks = table.Column<int>(type: "INTEGER", nullable: false),
                    MinifigCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseYear = table.Column<int>(type: "INTEGER", nullable: false),
                    DateModified = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OwnCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BuildCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ManualUrl = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sets", x => x.SetId);
                });

            migrationBuilder.CreateTable(
                name: "SetBricks",
                columns: table => new
                {
                    PartNum = table.Column<string>(type: "TEXT", nullable: false),
                    ColorId = table.Column<string>(type: "TEXT", nullable: false),
                    SetId = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    SpareCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetBricks", x => new { x.PartNum, x.ColorId, x.SetId });
                    table.ForeignKey(
                        name: "FK_SetBricks_Bricks_PartNum_ColorId",
                        columns: x => new { x.PartNum, x.ColorId },
                        principalTable: "Bricks",
                        principalColumns: new[] { "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetBricks_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "SetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SetMinifig",
                columns: table => new
                {
                    MinifigId = table.Column<string>(type: "TEXT", nullable: false),
                    SetId = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetMinifig", x => new { x.MinifigId, x.SetId });
                    table.ForeignKey(
                        name: "FK_SetMinifig_Minifigs_MinifigId",
                        column: x => x.MinifigId,
                        principalTable: "Minifigs",
                        principalColumn: "MinifigId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetMinifig_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "SetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SetBricks_SetId",
                table: "SetBricks",
                column: "SetId");

            migrationBuilder.CreateIndex(
                name: "IX_SetMinifig_SetId",
                table: "SetMinifig",
                column: "SetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SetBricks");

            migrationBuilder.DropTable(
                name: "SetMinifig");

            migrationBuilder.DropTable(
                name: "Bricks");

            migrationBuilder.DropTable(
                name: "Minifigs");

            migrationBuilder.DropTable(
                name: "Sets");
        }
    }
}
