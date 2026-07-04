using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Klods.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bricks",
                columns: table => new
                {
                    PartNum = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PartURL = table.Column<string>(type: "text", nullable: true),
                    PartImg = table.Column<string>(type: "text", nullable: true),
                    ColorName = table.Column<string>(type: "text", nullable: true),
                    HexColor = table.Column<string>(type: "text", nullable: true),
                    IsTrans = table.Column<bool>(type: "boolean", nullable: false),
                    BricklinkId = table.Column<string>(type: "text", nullable: true),
                    BrickOwlId = table.Column<string>(type: "text", nullable: true),
                    ElementId = table.Column<string>(type: "text", nullable: true),
                    PartCatId = table.Column<int>(type: "integer", nullable: true),
                    SetCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bricks", x => new { x.PartNum, x.ColorId });
                });

            migrationBuilder.CreateTable(
                name: "CatalogImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SnapshotDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Colors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Hex = table.Column<string>(type: "text", nullable: false),
                    IsTrans = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Colors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Minifigs",
                columns: table => new
                {
                    MinifigId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ImgUrl = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: true),
                    NumParts = table.Column<int>(type: "integer", nullable: false),
                    DateModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Minifigs", x => x.MinifigId);
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

            migrationBuilder.CreateTable(
                name: "Sets",
                columns: table => new
                {
                    SetId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SetURL = table.Column<string>(type: "text", nullable: true),
                    SetImg = table.Column<string>(type: "text", nullable: true),
                    NumBricks = table.Column<int>(type: "integer", nullable: false),
                    ReleaseYear = table.Column<int>(type: "integer", nullable: false),
                    DateModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ManualUrl = table.Column<string>(type: "text", nullable: false),
                    ThemeId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sets", x => x.SetId);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Themes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Themes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    ProfilePictureUrl = table.Column<string>(type: "text", nullable: true),
                    PrimaryColor = table.Column<string>(type: "text", nullable: true),
                    FontScale = table.Column<double>(type: "double precision", nullable: false, defaultValue: 1.0),
                    HasSeenTour = table.Column<bool>(type: "boolean", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false, defaultValue: "User"),
                    Status = table.Column<string>(type: "text", nullable: false, defaultValue: "Active")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "MinifigBricks",
                columns: table => new
                {
                    MinifigId = table.Column<string>(type: "text", nullable: false),
                    PartNum = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    SpareCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinifigBricks", x => new { x.MinifigId, x.PartNum, x.ColorId });
                    table.ForeignKey(
                        name: "FK_MinifigBricks_Bricks_PartNum_ColorId",
                        columns: x => new { x.PartNum, x.ColorId },
                        principalTable: "Bricks",
                        principalColumns: new[] { "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MinifigBricks_Minifigs_MinifigId",
                        column: x => x.MinifigId,
                        principalTable: "Minifigs",
                        principalColumn: "MinifigId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SetBricks",
                columns: table => new
                {
                    SetId = table.Column<string>(type: "text", nullable: false),
                    PartNum = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    SpareCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetBricks", x => new { x.SetId, x.PartNum, x.ColorId });
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
                    SetId = table.Column<string>(type: "text", nullable: false),
                    MinifigId = table.Column<string>(type: "text", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetMinifig", x => new { x.SetId, x.MinifigId });
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
                name: "SetsOwned",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SetId = table.Column<string>(type: "text", nullable: false),
                    SetIndex = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetsOwned", x => new { x.UserId, x.SetId, x.SetIndex });
                    table.ForeignKey(
                        name: "FK_SetsOwned_Sets_SetId",
                        column: x => x.SetId,
                        principalTable: "Sets",
                        principalColumn: "SetId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetsOwned_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserExternalLogins",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserExternalLogins", x => new { x.Provider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserExternalLogins_Users_UserId",
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
                    MinifigIndex = table.Column<int>(type: "integer", nullable: false),
                    SetId = table.Column<string>(type: "text", nullable: true),
                    SetIndex = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinifigOwned", x => new { x.UserId, x.MinifigId, x.MinifigIndex });
                    table.ForeignKey(
                        name: "FK_MinifigOwned_Minifigs_MinifigId",
                        column: x => x.MinifigId,
                        principalTable: "Minifigs",
                        principalColumn: "MinifigId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MinifigOwned_SetsOwned_UserId_SetId_SetIndex",
                        columns: x => new { x.UserId, x.SetId, x.SetIndex },
                        principalTable: "SetsOwned",
                        principalColumns: new[] { "UserId", "SetId", "SetIndex" });
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
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SetId = table.Column<string>(type: "text", nullable: false),
                    SetIndex = table.Column<int>(type: "integer", nullable: false),
                    PartNum = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    Stock = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetBrickOwned", x => new { x.UserId, x.SetId, x.SetIndex, x.PartNum, x.ColorId });
                    table.ForeignKey(
                        name: "FK_SetBrickOwned_SetBricks_SetId_PartNum_ColorId",
                        columns: x => new { x.SetId, x.PartNum, x.ColorId },
                        principalTable: "SetBricks",
                        principalColumns: new[] { "SetId", "PartNum", "ColorId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetBrickOwned_SetsOwned_UserId_SetId_SetIndex",
                        columns: x => new { x.UserId, x.SetId, x.SetIndex },
                        principalTable: "SetsOwned",
                        principalColumns: new[] { "UserId", "SetId", "SetIndex" },
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_BrickOwned_PartNum_ColorId",
                table: "BrickOwned",
                columns: new[] { "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_MinifigBrickOwned_MinifigId_PartNum_ColorId",
                table: "MinifigBrickOwned",
                columns: new[] { "MinifigId", "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_MinifigBricks_PartNum_ColorId",
                table: "MinifigBricks",
                columns: new[] { "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_MinifigOwned_MinifigId",
                table: "MinifigOwned",
                column: "MinifigId");

            migrationBuilder.CreateIndex(
                name: "IX_MinifigOwned_UserId_SetId_SetIndex",
                table: "MinifigOwned",
                columns: new[] { "UserId", "SetId", "SetIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_SetBrickOwned_SetId_PartNum_ColorId",
                table: "SetBrickOwned",
                columns: new[] { "SetId", "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_SetBricks_PartNum_ColorId",
                table: "SetBricks",
                columns: new[] { "PartNum", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_SetMinifig_MinifigId",
                table: "SetMinifig",
                column: "MinifigId");

            migrationBuilder.CreateIndex(
                name: "IX_SetsOwned_SetId",
                table: "SetsOwned",
                column: "SetId");

            migrationBuilder.CreateIndex(
                name: "IX_UserExternalLogins_UserId",
                table: "UserExternalLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrickOwned");

            migrationBuilder.DropTable(
                name: "CatalogImports");

            migrationBuilder.DropTable(
                name: "Colors");

            migrationBuilder.DropTable(
                name: "MinifigBrickOwned");

            migrationBuilder.DropTable(
                name: "PartCategories");

            migrationBuilder.DropTable(
                name: "SetBrickOwned");

            migrationBuilder.DropTable(
                name: "SetMinifig");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Themes");

            migrationBuilder.DropTable(
                name: "UserExternalLogins");

            migrationBuilder.DropTable(
                name: "MinifigBricks");

            migrationBuilder.DropTable(
                name: "MinifigOwned");

            migrationBuilder.DropTable(
                name: "SetBricks");

            migrationBuilder.DropTable(
                name: "Minifigs");

            migrationBuilder.DropTable(
                name: "SetsOwned");

            migrationBuilder.DropTable(
                name: "Bricks");

            migrationBuilder.DropTable(
                name: "Sets");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
