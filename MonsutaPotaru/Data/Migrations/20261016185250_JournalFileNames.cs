using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>
    /// Nom du fichier de la sauvegarde dans les libellés du journal enregistrés avant WalletOperation.SaveLabelOf
    /// (« Rouge / Bleu — Red » devient « Rouge / Bleu — Red\nrouge.sav ») : celui qu'a la sauvegarde aujourd'hui, si elle
    /// existe encore. Pas de changement de modèle.
    /// </summary>
    public partial class JournalFileNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE WalletOperations
                SET SaveLabel = SaveLabel || char(10) || (SELECT s.FileName FROM StoredSaves s WHERE s.Id = WalletOperations.StoredSaveId)
                WHERE StoredSaveId IS NOT NULL AND SaveLabel <> '' AND instr(SaveLabel, char(10)) = 0
                  AND EXISTS (SELECT 1 FROM StoredSaves s WHERE s.Id = WalletOperations.StoredSaveId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE WalletOperations SET SaveLabel = substr(SaveLabel, 1, instr(SaveLabel, char(10)) - 1) WHERE instr(SaveLabel, char(10)) > 0;");
        }
    }
}
