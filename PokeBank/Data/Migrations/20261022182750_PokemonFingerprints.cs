using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeBank.Migrations
{
    /// <summary>Empreinte des Pokémon (doublons d'une vieille copie de sauvegarde), remplie au démarrage pour les plus anciens.</summary>
    public partial class PokemonFingerprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "BankPokemon",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankPokemon_OwnerId_Fingerprint",
                table: "BankPokemon",
                columns: new[] { "OwnerId", "Fingerprint" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BankPokemon_OwnerId_Fingerprint",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "BankPokemon");
        }
    }
}
