using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>Pokémon installés dans le « Parc » de leur dresseur (30 au plus) ; supprimés avec le Pokémon.</summary>
    public partial class Park : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParkMembers",
                columns: table => new
                {
                    BankPokemonId = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkMembers", x => x.BankPokemonId);
                    table.ForeignKey(
                        name: "FK_ParkMembers_BankPokemon_BankPokemonId",
                        column: x => x.BankPokemonId,
                        principalTable: "BankPokemon",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParkMembers_OwnerId",
                table: "ParkMembers",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParkMembers");
        }
    }
}
