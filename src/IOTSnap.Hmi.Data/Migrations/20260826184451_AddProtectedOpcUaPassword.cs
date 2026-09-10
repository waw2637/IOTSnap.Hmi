using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProtectedOpcUaPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProtectedPassword",
                table: "OpcUaConnectionProfiles",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectedPassword",
                table: "OpcUaConnectionProfiles");
        }
    }
}
