using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddUserOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Data preservation: save per-instance stock before restructuring ──────
            migrationBuilder.Sql(@"
                CREATE TEMP TABLE ""SetBrickStock"" AS
                SELECT ""SetId"", ""SetIndex"", ""PartNum"", ""ColorId"", ""Stock""
                FROM ""SetBricks"";
            ");

            // Deduplicate SetBricks: keep one BOM row per (SetId, PartNum, ColorId).
            // Remove rows with duplicate (SetId, PartNum, ColorId), keeping the min ctid per group.
            migrationBuilder.Sql(@"
                DELETE FROM ""SetBricks""
                WHERE ctid NOT IN (
                    SELECT MIN(ctid)
                    FROM ""SetBricks""
                    GROUP BY ""SetId"", ""PartNum"", ""ColorId""
                );
            ");

            // Deduplicate SetMinifig: keep one BOM row per (SetId, MinifigId)
            migrationBuilder.Sql(@"
                DELETE FROM ""SetMinifig""
                WHERE ctid NOT IN (
                    SELECT MIN(ctid)
                    FROM ""SetMinifig""
                    GROUP BY ""SetId"", ""MinifigId""
                );
            ");
            // ─────────────────────────────────────────────────────────────────────────

            migrationBuilder.DropForeignKey(
                name: "FK_SetBricks_SetsOwned_SetId_SetIndex",
                table: "SetBricks");

            migrationBuilder.DropForeignKey(
                name: "FK_SetMinifig_SetsOwned_SetId_SetIndex",
                table: "SetMinifig");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetMinifig",
                table: "SetMinifig");

            migrationBuilder.DropIndex(
                name: "IX_SetMinifig_SetIndex",
                table: "SetMinifig");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetBricks",
                table: "SetBricks");

            migrationBuilder.DropIndex(
                name: "IX_SetBricks_OwnedSetIndex",
                table: "SetBricks");

            migrationBuilder.DropColumn(
                name: "SetIndex",
                table: "SetMinifig");

            migrationBuilder.DropColumn(
                name: "Stock",
                table: "SetMinifig");

            migrationBuilder.DropColumn(
                name: "SetIndex",
                table: "SetBricks");

            migrationBuilder.DropColumn(
                name: "Stock",
                table: "SetBricks");

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "SetsOwned",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetMinifig",
                table: "SetMinifig",
                columns: new[] { "SetId", "MinifigId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetBricks",
                table: "SetBricks",
                columns: new[] { "SetId", "PartNum", "ColorId" });

            migrationBuilder.CreateTable(
                name: "BrickOwned",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PartNum = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    Stock = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrickOwned", x => new { x.UserId, x.PartNum, x.ColorId });
                    table.ForeignKey(
                        name: "FK_BrickOwned_Bricks_PartNum_ColorId",
                        columns: x => new { x.PartNum, x.ColorId },
                        principalTable: "Bricks",
                        principalColumns: new[] { "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrickOwned_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MinifigOwned",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    MinifigId = table.Column<string>(type: "text", nullable: false),
                    Stock = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinifigOwned", x => new { x.UserId, x.MinifigId });
                    table.ForeignKey(
                        name: "FK_MinifigOwned_Minifigs_MinifigId",
                        column: x => x.MinifigId,
                        principalTable: "Minifigs",
                        principalColumn: "MinifigId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MinifigOwned_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SetBrickOwned",
                columns: table => new
                {
                    SetId = table.Column<string>(type: "text", nullable: false),
                    SetIndex = table.Column<int>(type: "integer", nullable: false),
                    PartNum = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    Stock = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetBrickOwned", x => new { x.SetId, x.SetIndex, x.PartNum, x.ColorId });
                    table.ForeignKey(
                        name: "FK_SetBrickOwned_SetBricks_SetId_PartNum_ColorId",
                        columns: x => new { x.SetId, x.PartNum, x.ColorId },
                        principalTable: "SetBricks",
                        principalColumns: new[] { "SetId", "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetBrickOwned_SetsOwned_SetId_SetIndex",
                        columns: x => new { x.SetId, x.SetIndex },
                        principalTable: "SetsOwned",
                        principalColumns: new[] { "SetId", "SetIndex" },
                        onDelete: ReferentialAction.Cascade);
                });

            // ── Restore per-instance stock into SetBrickOwned ─────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""SetBrickOwned"" (""SetId"", ""SetIndex"", ""PartNum"", ""ColorId"", ""Stock"")
                SELECT s.""SetId"", s.""SetIndex"", s.""PartNum"", s.""ColorId"", s.""Stock""
                FROM ""SetBrickStock"" s
                WHERE EXISTS (
                    SELECT 1 FROM ""SetBricks"" sb
                    WHERE sb.""SetId"" = s.""SetId""
                      AND sb.""PartNum"" = s.""PartNum""
                      AND sb.""ColorId"" = s.""ColorId""
                )
                AND EXISTS (
                    SELECT 1 FROM ""SetsOwned"" so
                    WHERE so.""SetId"" = s.""SetId""
                      AND so.""SetIndex"" = s.""SetIndex""
                );

                DROP TABLE ""SetBrickStock"";
            ");
            // ─────────────────────────────────────────────────────────────────────────

            migrationBuilder.CreateIndex(
                name: "IX_SetsOwned_UserId",
                table: "SetsOwned",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SetMinifig_MinifigId",
                table: "SetMinifig",
                column: "MinifigId");

            migrationBuilder.CreateIndex(
                name: "IX_SetBricks_PartNum_ColorId",
                table: "SetBricks",
                columns: new[] { "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_BrickOwned_PartNum_ColorId",
                table: "BrickOwned",
                columns: new[] { "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_MinifigOwned_MinifigId",
                table: "MinifigOwned",
                column: "MinifigId");

            migrationBuilder.CreateIndex(
                name: "IX_SetBrickOwned_SetId_PartNum_ColorId",
                table: "SetBrickOwned",
                columns: new[] { "SetId", "PartNum", "ColorId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SetBricks_Sets_SetId",
                table: "SetBricks",
                column: "SetId",
                principalTable: "Sets",
                principalColumn: "SetId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SetMinifig_Sets_SetId",
                table: "SetMinifig",
                column: "SetId",
                principalTable: "Sets",
                principalColumn: "SetId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SetsOwned_Users_UserId",
                table: "SetsOwned",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SetBricks_Sets_SetId",
                table: "SetBricks");

            migrationBuilder.DropForeignKey(
                name: "FK_SetMinifig_Sets_SetId",
                table: "SetMinifig");

            migrationBuilder.DropForeignKey(
                name: "FK_SetsOwned_Users_UserId",
                table: "SetsOwned");

            migrationBuilder.DropTable(
                name: "BrickOwned");

            migrationBuilder.DropTable(
                name: "MinifigOwned");

            migrationBuilder.DropTable(
                name: "SetBrickOwned");

            migrationBuilder.DropIndex(
                name: "IX_SetsOwned_UserId",
                table: "SetsOwned");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetMinifig",
                table: "SetMinifig");

            migrationBuilder.DropIndex(
                name: "IX_SetMinifig_MinifigId",
                table: "SetMinifig");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetBricks",
                table: "SetBricks");

            migrationBuilder.DropIndex(
                name: "IX_SetBricks_PartNum_ColorId",
                table: "SetBricks");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "SetsOwned");

            migrationBuilder.AddColumn<int>(
                name: "SetIndex",
                table: "SetMinifig",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Stock",
                table: "SetMinifig",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SetIndex",
                table: "SetBricks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Stock",
                table: "SetBricks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetMinifig",
                table: "SetMinifig",
                columns: new[] { "MinifigId", "SetId", "SetIndex" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetBricks",
                table: "SetBricks",
                columns: new[] { "PartNum", "ColorId", "SetId", "SetIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_SetMinifig_SetId_SetIndex",
                table: "SetMinifig",
                columns: new[] { "SetId", "SetIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_SetBricks_SetId_SetIndex",
                table: "SetBricks",
                columns: new[] { "SetId", "SetIndex" });

            migrationBuilder.AddForeignKey(
                name: "FK_SetBricks_SetsOwned_SetId_SetIndex",
                table: "SetBricks",
                columns: new[] { "SetId", "SetIndex" },
                principalTable: "SetsOwned",
                principalColumns: new[] { "SetId", "SetIndex" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SetMinifig_SetsOwned_SetId_SetIndex",
                table: "SetMinifig",
                columns: new[] { "SetId", "SetIndex" },
                principalTable: "SetsOwned",
                principalColumns: new[] { "SetId", "SetIndex" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
