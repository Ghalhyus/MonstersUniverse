using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>
    /// Caractéristiques des Pokémon pour les filtres de « Ma collection » (types, nature, talent, IV, forme, Méga). Les lignes
    /// existantes sont remplies au démarrage par BankService.FillFilterDetailsAsync (repère : nature null).
    /// </summary>
    public partial class CollectionFilters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ability",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "FormKind",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "IvAtk",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "IvDef",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "IvHp",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "IvSpA",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "IvSpD",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "IvSpe",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "MegaReady",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "Nature",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Type1",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "Type2",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ability",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "FormKind",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "IvAtk",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "IvDef",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "IvHp",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "IvSpA",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "IvSpD",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "IvSpe",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "MegaReady",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "Nature",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "Type1",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "Type2",
                table: "BankPokemon");
        }
    }
}
