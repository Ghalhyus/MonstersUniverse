using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MonsutaPotaru.Migrations
{
    /// <summary>
    /// Rôles Standard et Administrateur (identifiants fixes), Standard pour tous les comptes existants, et table des
    /// réglages du site (code d'invitation, rempli au démarrage : AdminService.InitializeAsync).
    /// </summary>
    public partial class RolesAndInviteCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Key);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { "administrateur", "administrateur", "Administrateur", "ADMINISTRATEUR" },
                    { "standard", "standard", "Standard", "STANDARD" }
                });

            migrationBuilder.Sql("INSERT INTO AspNetUserRoles (UserId, RoleId) SELECT Id, 'standard' FROM AspNetUsers;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM AspNetUserRoles WHERE RoleId IN ('standard', 'administrateur');");

            migrationBuilder.DropTable(
                name: "SiteSettings");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "administrateur");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "standard");
        }
    }
}
