using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>Parc : agrandissements achetés (0 à 3 ; capacité 25 + 25 par agrandissement).</summary>
    public partial class ParkExpansions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParkExpansions",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParkExpansions",
                table: "AspNetUsers");
        }
    }
}
