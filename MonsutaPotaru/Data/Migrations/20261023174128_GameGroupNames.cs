using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>
    /// Noms des groupes de jeux de 3e et 4e génération enregistrés avant leur entrée dans PokemonInfo.GroupNames (« HGSS »
    /// devient « Or HeartGold / Argent SoulSilver ») : sauvegardes stockées, libellés du journal (« HGSS — Ghal ») et jeu
    /// d'origine des Pokémon. Même principe que GameNames. Pas de changement de modèle.
    /// </summary>
    public partial class GameGroupNames : Migration
    {
        private static readonly (string Code, string Name)[] Names =
        [
            ("RS", "Rubis / Saphir"),
            ("RSE", "Rubis / Saphir / Émeraude"),
            ("FRLG", "Rouge Feu / Vert Feuille"),
            ("COLO", "Colosseum"),
            ("XD", "XD : Le Souffle des Ténèbres"),
            ("RSBOX", "Pokémon Box"),
            ("DP", "Diamant / Perle"),
            ("DPPt", "Diamant / Perle / Platine"),
            ("HGSS", "Or HeartGold / Argent SoulSilver"),
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
            // Libellé « jeu — dresseur » : seul le début change (« RS — » ne prend pas « RSE — »).
            migrationBuilder.Sql(
                $"UPDATE WalletOperations SET SaveLabel = '{to}' || substr(SaveLabel, {from.Length + 1}) WHERE SaveLabel LIKE '{from} — %';");
        }
    }
}
