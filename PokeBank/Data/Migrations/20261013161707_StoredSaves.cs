using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeBank.Migrations
{
    /// <inheritdoc />
    public partial class StoredSaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "BankPokemon",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StoredSaveId",
                table: "BankPokemon",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoredSaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    IdentityKey = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    GameName = table.Column<string>(type: "TEXT", nullable: false),
                    Generation = table.Column<byte>(type: "INTEGER", nullable: false),
                    TrainerName = table.Column<string>(type: "TEXT", nullable: false),
                    TrainerId = table.Column<string>(type: "TEXT", nullable: false),
                    PlayTimeSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    PokemonCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredSaves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredSaves_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankPokemon_StoredSaveId",
                table: "BankPokemon",
                column: "StoredSaveId");

            migrationBuilder.CreateIndex(
                name: "IX_StoredSaves_IdentityKey",
                table: "StoredSaves",
                column: "IdentityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredSaves_OwnerId",
                table: "StoredSaves",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_BankPokemon_StoredSaves_StoredSaveId",
                table: "BankPokemon",
                column: "StoredSaveId",
                principalTable: "StoredSaves",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankPokemon_StoredSaves_StoredSaveId",
                table: "BankPokemon");

            migrationBuilder.DropTable(
                name: "StoredSaves");

            migrationBuilder.DropIndex(
                name: "IX_BankPokemon_StoredSaveId",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "BankPokemon");

            migrationBuilder.DropColumn(
                name: "StoredSaveId",
                table: "BankPokemon");
        }
    }
}
