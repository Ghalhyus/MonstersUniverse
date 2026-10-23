using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>
    /// Noms des jeux de 1re et 2e génération enregistrés avant PokemonInfo.GroupNames (« RB » devient « Rouge / Bleu ») :
    /// sauvegardes stockées, libellés du journal (« RB — Red ») et jeu d'origine des Pokémon. Pas de changement de modèle.
    /// </summary>
    public partial class GameNames : Migration
    {
        private static readonly (string Code, string Name)[] Names =
        [
            ("RB", "Rouge / Bleu"),
            ("RBY", "Rouge / Bleu / Jaune"),
            ("GS", "Or / Argent"),
            ("GSC", "Or / Argent / Cristal"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, name) in Names)
                Rename(migrationBuilder, code, name);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (code, name) in Names)
                Rename(migrationBuilder, name, code);
        }

        private static void Rename(MigrationBuilder migrationBuilder, string from, string to)
        {
            migrationBuilder.Sql($"UPDATE StoredSaves SET GameName = '{to}' WHERE GameName = '{from}';");
            migrationBuilder.Sql($"UPDATE BankPokemon SET OriginGame = '{to}' WHERE OriginGame = '{from}';");
            // Libellé « jeu — dresseur » : seul le début change (« RB — » ne prend pas « RBY — »).
            migrationBuilder.Sql(
                $"UPDATE WalletOperations SET SaveLabel = '{to}' || substr(SaveLabel, {from.Length + 1}) WHERE SaveLabel LIKE '{from} — %';");
        }
    }
}
