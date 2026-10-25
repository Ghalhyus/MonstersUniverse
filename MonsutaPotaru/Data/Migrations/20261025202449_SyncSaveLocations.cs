using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <summary>Emplacement de chaque sauvegarde stockée sur chaque console reliée (récupération depuis la 3DS).</summary>
    public partial class SyncSaveLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncSaveLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                    StoredSaveId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncSaveLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncSaveLocations_StoredSaves_StoredSaveId",
                        column: x => x.StoredSaveId,
                        principalTable: "StoredSaves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SyncSaveLocations_SyncDevices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "SyncDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncSaveLocations_DeviceId_Key",
                table: "SyncSaveLocations",
                columns: new[] { "DeviceId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncSaveLocations_StoredSaveId",
                table: "SyncSaveLocations",
                column: "StoredSaveId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncSaveLocations");
        }
    }
}
