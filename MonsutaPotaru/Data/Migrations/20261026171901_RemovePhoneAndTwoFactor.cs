using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>
    /// Téléphone et double authentification retirés du site : numéros effacés, double authentification coupée, clés
    /// d'application d'authentification et codes de secours supprimés. Rien à remettre au retour en arrière.
    /// </summary>
    public partial class RemovePhoneAndTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE AspNetUsers SET PhoneNumber = NULL, PhoneNumberConfirmed = 0, TwoFactorEnabled = 0;");
            migrationBuilder.Sql("DELETE FROM AspNetUserTokens WHERE LoginProvider = '[AspNetUserStore]' AND Name IN ('AuthenticatorKey', 'RecoveryCodes');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
