using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <inheritdoc />
    public partial class CollectionLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "WalletOperations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CollectionLimit",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000); // comptes existants : limite de départ (CollectionCapacity.DefaultLimit)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Details",
                table: "WalletOperations");

            migrationBuilder.DropColumn(
                name: "CollectionLimit",
                table: "AspNetUsers");
        }
    }
}
