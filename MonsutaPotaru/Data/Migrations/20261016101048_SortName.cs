using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>
    /// Nom de tri sans accents des Pokémon (tri par nom de « Ma collection »). Les lignes existantes sont remplies au
    /// démarrage par BankService.FillSortNamesAsync : SQLite ne sait pas retirer les accents.
    /// </summary>
    public partial class SortName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SortName",
                table: "BankPokemon",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortName",
                table: "BankPokemon");
        }
    }
}
