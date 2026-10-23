using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MonsutaPotaru.Migrations
{
    /// <inheritdoc />
    public partial class BallInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BallsChanged",
                table: "SaveModifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DataHash",
                table: "SaveModifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OwnedItems",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnedItems", x => new { x.OwnerId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_OwnedItems_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnedItems");

            migrationBuilder.DropColumn(
                name: "BallsChanged",
                table: "SaveModifications");

            migrationBuilder.DropColumn(
                name: "DataHash",
                table: "SaveModifications");
        }
    }
}
