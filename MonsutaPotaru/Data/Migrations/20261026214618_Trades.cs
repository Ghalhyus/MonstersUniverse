using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>Échanges : journal des échanges réussis (statistiques), et l'échange noté sur la trace des modifications du serveur.</summary>
    public partial class Trades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PokemonTraded",
                table: "SaveModifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TradeRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstId = table.Column<string>(type: "TEXT", nullable: false),
                    SecondId = table.Column<string>(type: "TEXT", nullable: false),
                    FirstGave = table.Column<ushort>(type: "INTEGER", nullable: false),
                    FirstGaveEvolvedTo = table.Column<ushort>(type: "INTEGER", nullable: true),
                    SecondGave = table.Column<ushort>(type: "INTEGER", nullable: false),
                    SecondGaveEvolvedTo = table.Column<ushort>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecords_FirstId",
                table: "TradeRecords",
                column: "FirstId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRecords_SecondId",
                table: "TradeRecords",
                column: "SecondId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TradeRecords");

            migrationBuilder.DropColumn(
                name: "PokemonTraded",
                table: "SaveModifications");
        }
    }
}
