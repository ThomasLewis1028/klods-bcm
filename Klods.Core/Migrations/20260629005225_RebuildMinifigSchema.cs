using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klods.Migrations
{
    /// <inheritdoc />
    public partial class RebuildMinifigSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MinifigBricks_Bricks_BrickID_ColorId",
                table: "MinifigBricks");

            migrationBuilder.DropForeignKey(
                name: "FK_MinifigBricks_Minifigs_MinifigID",
                table: "MinifigBricks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MinifigOwned",
                table: "MinifigOwned");

            migrationBuilder.DropColumn(
                name: "Stock",
                table: "MinifigOwned");

            migrationBuilder.RenameColumn(
                name: "MinifigName",
                table: "Minifigs",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "MinifigUrl",
                table: "Minifigs",
                newName: "Url");

            migrationBuilder.RenameColumn(
                name: "MinifigImgUrl",
                table: "Minifigs",
                newName: "ImgUrl");

            migrationBuilder.RenameColumn(
                name: "MinifigID",
                table: "MinifigBricks",
                newName: "MinifigId");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "MinifigBricks",
                newName: "Count");

            migrationBuilder.RenameColumn(
                name: "BrickID",
                table: "MinifigBricks",
                newName: "PartNum");

            migrationBuilder.RenameIndex(
                name: "IX_MinifigBricks_BrickID_ColorId",
                table: "MinifigBricks",
                newName: "IX_MinifigBricks_PartNum_ColorId");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateModified",
                table: "Minifigs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "NumParts",
                table: "Minifigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinifigIndex",
                table: "MinifigOwned",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SetId",
                table: "MinifigOwned",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SetIndex",
                table: "MinifigOwned",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpareCount",
                table: "MinifigBricks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BrickOwlId",
                table: "Bricks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElementId",
                table: "Bricks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartCatId",
                table: "Bricks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_MinifigOwned",
                table: "MinifigOwned",
                columns: new[] { "UserId", "MinifigId", "MinifigIndex" });

            migrationBuilder.CreateTable(
                name: "MinifigBrickOwned",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MinifigId = table.Column<string>(type: "text", nullable: false),
                    MinifigIndex = table.Column<int>(type: "integer", nullable: false),
                    PartNum = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    Stock = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinifigBrickOwned", x => new { x.UserId, x.MinifigId, x.MinifigIndex, x.PartNum, x.ColorId });
                    table.ForeignKey(
                        name: "FK_MinifigBrickOwned_MinifigBricks_MinifigId_PartNum_ColorId",
                        columns: x => new { x.MinifigId, x.PartNum, x.ColorId },
                        principalTable: "MinifigBricks",
                        principalColumns: new[] { "MinifigId", "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MinifigBrickOwned_MinifigOwned_UserId_MinifigId_MinifigIndex",
                        columns: x => new { x.UserId, x.MinifigId, x.MinifigIndex },
                        principalTable: "MinifigOwned",
                        principalColumns: new[] { "UserId", "MinifigId", "MinifigIndex" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MinifigOwned_UserId_SetId_SetIndex",
                table: "MinifigOwned",
                columns: new[] { "UserId", "SetId", "SetIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_MinifigBrickOwned_MinifigId_PartNum_ColorId",
                table: "MinifigBrickOwned",
                columns: new[] { "MinifigId", "PartNum", "ColorId" });

            migrationBuilder.AddForeignKey(
                name: "FK_MinifigBricks_Bricks_PartNum_ColorId",
                table: "MinifigBricks",
                columns: new[] { "PartNum", "ColorId" },
                principalTable: "Bricks",
                principalColumns: new[] { "PartNum", "ColorId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MinifigBricks_Minifigs_MinifigId",
                table: "MinifigBricks",
                column: "MinifigId",
                principalTable: "Minifigs",
                principalColumn: "MinifigId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MinifigOwned_SetsOwned_UserId_SetId_SetIndex",
                table: "MinifigOwned",
                columns: new[] { "UserId", "SetId", "SetIndex" },
                principalTable: "SetsOwned",
                principalColumns: new[] { "UserId", "SetId", "SetIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MinifigBricks_Bricks_PartNum_ColorId",
                table: "MinifigBricks");

            migrationBuilder.DropForeignKey(
                name: "FK_MinifigBricks_Minifigs_MinifigId",
                table: "MinifigBricks");

            migrationBuilder.DropForeignKey(
                name: "FK_MinifigOwned_SetsOwned_UserId_SetId_SetIndex",
                table: "MinifigOwned");

            migrationBuilder.DropTable(
                name: "MinifigBrickOwned");

            migrationBuilder.DropTable(
                name: "PartCategories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MinifigOwned",
                table: "MinifigOwned");

            migrationBuilder.DropIndex(
                name: "IX_MinifigOwned_UserId_SetId_SetIndex",
                table: "MinifigOwned");

            migrationBuilder.DropColumn(
                name: "DateModified",
                table: "Minifigs");

            migrationBuilder.DropColumn(
                name: "ImgUrl",
                table: "Minifigs");

            migrationBuilder.DropColumn(
                name: "NumParts",
                table: "Minifigs");

            migrationBuilder.DropColumn(
                name: "MinifigIndex",
                table: "MinifigOwned");

            migrationBuilder.DropColumn(
                name: "SetId",
                table: "MinifigOwned");

            migrationBuilder.DropColumn(
                name: "SetIndex",
                table: "MinifigOwned");

            migrationBuilder.DropColumn(
                name: "SpareCount",
                table: "MinifigBricks");

            migrationBuilder.DropColumn(
                name: "BrickOwlId",
                table: "Bricks");

            migrationBuilder.DropColumn(
                name: "ElementId",
                table: "Bricks");

            migrationBuilder.DropColumn(
                name: "PartCatId",
                table: "Bricks");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "Minifigs",
                newName: "MinifigName");

            migrationBuilder.RenameColumn(
                name: "Url",
                table: "Minifigs",
                newName: "MinifigUrl");

            migrationBuilder.RenameColumn(
                name: "ImgUrl",
                table: "Minifigs",
                newName: "MinifigImgUrl");

            migrationBuilder.AddColumn<int>(
                name: "Stock",
                table: "MinifigOwned",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.RenameColumn(
                name: "MinifigId",
                table: "MinifigBricks",
                newName: "MinifigID");

            migrationBuilder.RenameColumn(
                name: "Count",
                table: "MinifigBricks",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "PartNum",
                table: "MinifigBricks",
                newName: "BrickID");

            migrationBuilder.RenameIndex(
                name: "IX_MinifigBricks_PartNum_ColorId",
                table: "MinifigBricks",
                newName: "IX_MinifigBricks_BrickID_ColorId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MinifigOwned",
                table: "MinifigOwned",
                columns: new[] { "UserId", "MinifigId" });

            migrationBuilder.AddForeignKey(
                name: "FK_MinifigBricks_Bricks_BrickID_ColorId",
                table: "MinifigBricks",
                columns: new[] { "BrickID", "ColorId" },
                principalTable: "Bricks",
                principalColumns: new[] { "PartNum", "ColorId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MinifigBricks_Minifigs_MinifigID",
                table: "MinifigBricks",
                column: "MinifigID",
                principalTable: "Minifigs",
                principalColumn: "MinifigId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
