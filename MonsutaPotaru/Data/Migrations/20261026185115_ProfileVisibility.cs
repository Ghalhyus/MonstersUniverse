using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>Visibilité du profil (Multijoueurs) : public par défaut, avec les quatre pages (comptes existants compris).</summary>
    public partial class ProfileVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProfilePrivate",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PublicPages",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 15); // PublicPages.All : les comptes existants sont publics, avec leurs quatre pages
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilePrivate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PublicPages",
                table: "AspNetUsers");
        }
    }
}
