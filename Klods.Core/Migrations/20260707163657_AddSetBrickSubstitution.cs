using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Klods.Migrations
{
    /// <inheritdoc />
    public partial class AddSetBrickSubstitution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SetBrickSubstitutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SetId = table.Column<string>(type: "text", nullable: false),
                    SetIndex = table.Column<int>(type: "integer", nullable: false),
                    ReqPartNum = table.Column<string>(type: "text", nullable: false),
                    ReqColorId = table.Column<string>(type: "text", nullable: false),
                    SubPartNum = table.Column<string>(type: "text", nullable: false),
                    SubColorId = table.Column<string>(type: "text", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    PulledFromLoose = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetBrickSubstitutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetBrickSubstitutions_Bricks_SubPartNum_SubColorId",
                        columns: x => new { x.SubPartNum, x.SubColorId },
                        principalTable: "Bricks",
                        principalColumns: new[] { "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetBrickSubstitutions_SetBricks_SetId_ReqPartNum_ReqColorId",
                        columns: x => new { x.SetId, x.ReqPartNum, x.ReqColorId },
                        principalTable: "SetBricks",
                        principalColumns: new[] { "SetId", "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetBrickSubstitutions_SetsOwned_UserId_SetId_SetIndex",
                        columns: x => new { x.UserId, x.SetId, x.SetIndex },
                        principalTable: "SetsOwned",
                        principalColumns: new[] { "UserId", "SetId", "SetIndex" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SetBrickSubstitutions_SetId_ReqPartNum_ReqColorId",
                table: "SetBrickSubstitutions",
                columns: new[] { "SetId", "ReqPartNum", "ReqColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_SetBrickSubstitutions_SubPartNum_SubColorId",
                table: "SetBrickSubstitutions",
                columns: new[] { "SubPartNum", "SubColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_SetBrickSubstitutions_UserId_SetId_SetIndex",
                table: "SetBrickSubstitutions",
                columns: new[] { "UserId", "SetId", "SetIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SetBrickSubstitutions");
        }
    }
}
