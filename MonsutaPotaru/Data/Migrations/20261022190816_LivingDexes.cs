using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>Living dex des comptes (un par jeu, supprimé avec sa sauvegarde stockée).</summary>
    public partial class LivingDexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LivingDexes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Game = table.Column<int>(type: "INTEGER", nullable: false),
                    StoredSaveId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LivingDexes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LivingDexes_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LivingDexes_StoredSaves_StoredSaveId",
                        column: x => x.StoredSaveId,
                        principalTable: "StoredSaves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LivingDexes_OwnerId_Game",
                table: "LivingDexes",
                columns: new[] { "OwnerId", "Game" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LivingDexes_StoredSaveId",
                table: "LivingDexes",
                column: "StoredSaveId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LivingDexes");
        }
    }
}
