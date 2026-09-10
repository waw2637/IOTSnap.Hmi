using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlarmSeverity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Severity",
                table: "OpcUaAlarmStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Severity",
                table: "OpcUaAlarmStates");
        }
    }
}
