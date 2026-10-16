using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeBank.Migrations
{
    /// <summary>
    /// Sexe et objet tenu des Pokémon, pour leur image (femelle différente, Méga-Évolution). Les lignes existantes sont
    /// remplies au démarrage par BankService.FillSpriteDetailsAsync, qui relit leurs octets.
    /// </summary>
    public partial class SpriteDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Gender",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeldItem",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Gender",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "HeldItem",
                table: "BankPokemon");
        }
    }
}
