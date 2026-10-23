using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <inheritdoc />
    public partial class AddBankPokemon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankPokemon",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Context = table.Column<byte>(type: "INTEGER", nullable: false),
                    Extension = table.Column<string>(type: "TEXT", nullable: false),
                    Species = table.Column<ushort>(type: "INTEGER", nullable: false),
                    Form = table.Column<byte>(type: "INTEGER", nullable: false),
                    SpeciesName = table.Column<string>(type: "TEXT", nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<byte>(type: "INTEGER", nullable: false),
                    IsShiny = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEgg = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLegal = table.Column<bool>(type: "INTEGER", nullable: false),
                    OriginalTrainer = table.Column<string>(type: "TEXT", nullable: false),
                    OriginGame = table.Column<string>(type: "TEXT", nullable: false),
                    DepositedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankPokemon", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankPokemon_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankPokemon_OwnerId_DepositedAt",
                table: "BankPokemon",
                columns: new[] { "OwnerId", "DepositedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankPokemon");
        }
    }
}
