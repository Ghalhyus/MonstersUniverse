using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <inheritdoc />
    public partial class SaveModifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaveModifications",
                columns: table => new
                {
                    IdentityKey = table.Column<string>(type: "TEXT", nullable: false),
                    PlayTimeSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MoneyAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    Withdrawn = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaveModifications", x => x.IdentityKey);
                });

            // Reprend les retraits déjà faits avant de supprimer les anciennes colonnes. L'argent restant n'est connu que
            // dans le fichier : 0 par défaut, ce qui refuse à temps de jeu égal tout fichier contenant encore de l'argent.
            migrationBuilder.Sql("""
                INSERT INTO SaveModifications (IdentityKey, PlayTimeSeconds, MoneyAfter, Withdrawn, ModifiedAt)
                SELECT IdentityKey, PlayTimeSeconds, 0, WithdrawnOnServer, ModifiedOnServerAt
                FROM StoredSaves
                WHERE ModifiedOnServerAt IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ModifiedOnServerAt",
                table: "StoredSaves");

            migrationBuilder.DropColumn(
                name: "WithdrawnOnServer",
                table: "StoredSaves");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaveModifications");

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedOnServerAt",
                table: "StoredSaves",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "WithdrawnOnServer",
                table: "StoredSaves",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
