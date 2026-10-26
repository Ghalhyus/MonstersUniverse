using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>
    /// Amis : demandes d'ami, amitiés (dans les deux sens), code ami ; visibilité à trois valeurs à la place du profil privé
    /// (privé → « Personne » ; au retour, « Amis seulement » et « Personne » redeviennent privés).
    /// </summary>
    public partial class Friends : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProfilePrivate",
                table: "AspNetUsers",
                newName: "Audience");

            // L'ancien profil privé (1) devient « Personne » (ProfileAudience.Nobody = 2) ; public (0) reste « Tout le monde ».
            migrationBuilder.Sql("UPDATE AspNetUsers SET Audience = 2 WHERE Audience = 1;");

            migrationBuilder.AddColumn<string>(
                name: "FriendCode",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FriendRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FromId = table.Column<string>(type: "TEXT", nullable: false),
                    ToId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FriendRequests_AspNetUsers_FromId",
                        column: x => x.FromId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FriendRequests_AspNetUsers_ToId",
                        column: x => x.ToId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Friendships",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    FriendId = table.Column<string>(type: "TEXT", nullable: false),
                    Since = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendships", x => new { x.UserId, x.FriendId });
                    table.ForeignKey(
                        name: "FK_Friendships_AspNetUsers_FriendId",
                        column: x => x.FriendId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Friendships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_FriendCode",
                table: "AspNetUsers",
                column: "FriendCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequests_FromId_ToId",
                table: "FriendRequests",
                columns: new[] { "FromId", "ToId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequests_ToId",
                table: "FriendRequests",
                column: "ToId");

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_FriendId",
                table: "Friendships",
                column: "FriendId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FriendRequests");

            migrationBuilder.DropTable(
                name: "Friendships");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_FriendCode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FriendCode",
                table: "AspNetUsers");

            migrationBuilder.Sql("UPDATE AspNetUsers SET Audience = 1 WHERE Audience <> 0;"); // amis seulement ou personne → privé

            migrationBuilder.RenameColumn(
                name: "Audience",
                table: "AspNetUsers",
                newName: "ProfilePrivate");
        }
    }
}
