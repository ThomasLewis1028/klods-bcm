using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddUserToPKs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SetBrickOwned_SetsOwned_SetId_SetIndex",
                table: "SetBrickOwned");

            migrationBuilder.DropForeignKey(
                name: "FK_SetsOwned_Users_UserId",
                table: "SetsOwned");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetsOwned",
                table: "SetsOwned");

            migrationBuilder.DropIndex(
                name: "IX_SetsOwned_UserId",
                table: "SetsOwned");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetBrickOwned",
                table: "SetBrickOwned");

            // ── Data preservation: remove orphan rows before making UserId NOT NULL ──
            // Delete SetBrickOwned records for set-copies with no user
            migrationBuilder.Sql(@"
                DELETE FROM ""SetBrickOwned""
                WHERE (""SetId"", ""SetIndex"") IN (
                    SELECT ""SetId"", ""SetIndex"" FROM ""SetsOwned"" WHERE ""UserId"" IS NULL
                );
            ");
            // Delete SetsOwned records with no user
            migrationBuilder.Sql(@"
                DELETE FROM ""SetsOwned"" WHERE ""UserId"" IS NULL;
            ");
            // ─────────────────────────────────────────────────────────────────────────

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "SetsOwned",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "SetBrickOwned",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // ── Populate SetBrickOwned.UserId from SetsOwned ──────────────────────
            migrationBuilder.Sql(@"
                UPDATE ""SetBrickOwned"" sbo
                SET ""UserId"" = so.""UserId""
                FROM ""SetsOwned"" so
                WHERE so.""SetId"" = sbo.""SetId"" AND so.""SetIndex"" = sbo.""SetIndex"";
            ");
            // Delete any SetBrickOwned rows that had no matching SetsOwned (UserId still 0)
            migrationBuilder.Sql(@"
                DELETE FROM ""SetBrickOwned"" WHERE ""UserId"" = 0;
            ");
            // ─────────────────────────────────────────────────────────────────────────

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetsOwned",
                table: "SetsOwned",
                columns: new[] { "UserId", "SetId", "SetIndex" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetBrickOwned",
                table: "SetBrickOwned",
                columns: new[] { "UserId", "SetId", "SetIndex", "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_SetsOwned_SetId",
                table: "SetsOwned",
                column: "SetId");

            migrationBuilder.AddForeignKey(
                name: "FK_SetBrickOwned_SetsOwned_UserId_SetId_SetIndex",
                table: "SetBrickOwned",
                columns: new[] { "UserId", "SetId", "SetIndex" },
                principalTable: "SetsOwned",
                principalColumns: new[] { "UserId", "SetId", "SetIndex" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SetsOwned_Users_UserId",
                table: "SetsOwned",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SetBrickOwned_SetsOwned_UserId_SetId_SetIndex",
                table: "SetBrickOwned");

            migrationBuilder.DropForeignKey(
                name: "FK_SetsOwned_Users_UserId",
                table: "SetsOwned");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetsOwned",
                table: "SetsOwned");

            migrationBuilder.DropIndex(
                name: "IX_SetsOwned_SetId",
                table: "SetsOwned");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SetBrickOwned",
                table: "SetBrickOwned");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "SetBrickOwned");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "SetsOwned",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetsOwned",
                table: "SetsOwned",
                columns: new[] { "SetId", "SetIndex" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_SetBrickOwned",
                table: "SetBrickOwned",
                columns: new[] { "SetId", "SetIndex", "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_SetsOwned_UserId",
                table: "SetsOwned",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SetBrickOwned_SetsOwned_SetId_SetIndex",
                table: "SetBrickOwned",
                columns: new[] { "SetId", "SetIndex" },
                principalTable: "SetsOwned",
                principalColumns: new[] { "SetId", "SetIndex" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SetsOwned_Users_UserId",
                table: "SetsOwned",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId");
        }
    }
}
