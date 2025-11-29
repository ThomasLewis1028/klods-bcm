using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LEGO_Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddedDBContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SetsOwned",
                columns: table => new
                {
                    SetId = table.Column<string>(type: "text", nullable: false),
                    SetIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetsOwned", x => new { x.SetId, x.SetIndex });
                });

            migrationBuilder.Sql("insert into \"SetsOwned\" select \"SetId\", 0 from \"Sets\"");
            
            migrationBuilder.AddForeignKey(
                name: "FK_SetsOwned_Sets_SetId",
                table: "SetsOwned",
                column: "SetId",
                principalTable: "Sets",
                principalColumn: "SetId",
                onDelete: ReferentialAction.Cascade);
            
            migrationBuilder.AddColumn<int>(
                name: "SetIndex",
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
            
            migrationBuilder.DropForeignKey(
                name: "FK_SetBricks_Sets_SetId",
                table: "SetBricks");

            migrationBuilder.DropForeignKey(
                name: "FK_SetMinifig_Sets_SetId",
                table: "SetMinifig");

            migrationBuilder.CreateIndex(
                name: "IX_SetMinifig_SetIndex",
                table: "SetMinifig",
                columns: new[] { "SetIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_SetBricks_OwnedSetIndex",
                table: "SetBricks",
                columns: new[] { "SetIndex" });
            
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SetBricks_SetsOwned_OwnedSetId_OwnedSetIndex",
                table: "SetBricks");

            migrationBuilder.DropForeignKey(
                name: "FK_SetMinifig_SetsOwned_OwnedSetId_OwnedSetIndex",
                table: "SetMinifig");

            migrationBuilder.DropTable(
                name: "SetsOwned");

            migrationBuilder.DropIndex(
                name: "IX_SetMinifig_OwnedSetId_OwnedSetIndex",
                table: "SetMinifig");

            migrationBuilder.DropIndex(
                name: "IX_SetBricks_OwnedSetId_OwnedSetIndex",
                table: "SetBricks");

            migrationBuilder.DropColumn(
                name: "OwnedSetId",
                table: "SetMinifig");

            migrationBuilder.DropColumn(
                name: "OwnedSetIndex",
                table: "SetMinifig");

            migrationBuilder.DropColumn(
                name: "OwnedSetId",
                table: "SetBricks");

            migrationBuilder.DropColumn(
                name: "OwnedSetIndex",
                table: "SetBricks");
        }
    }
}
