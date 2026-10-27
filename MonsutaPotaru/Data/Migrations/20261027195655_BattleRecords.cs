using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>Combats : journal des combats terminés (statistiques : victoires, défaites, matchs nuls).</summary>
    public partial class BattleRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BattleRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstId = table.Column<string>(type: "TEXT", nullable: false),
                    SecondId = table.Column<string>(type: "TEXT", nullable: false),
                    Format = table.Column<string>(type: "TEXT", nullable: false),
                    WinnerId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BattleRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BattleRecords_FirstId",
                table: "BattleRecords",
                column: "FirstId");

            migrationBuilder.CreateIndex(
                name: "IX_BattleRecords_SecondId",
                table: "BattleRecords",
                column: "SecondId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BattleRecords");
        }
    }
}
