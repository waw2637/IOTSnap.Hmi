using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpcUaAlarmState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpcUaAlarmStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Area = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StatusCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastValueText = table.Column<string>(type: "TEXT", nullable: true),
                    AlarmText = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstRaisedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastRaisedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AcknowledgedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClearedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastUpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpcUaAlarmStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpcUaAlarmStates_NodeId",
                table: "OpcUaAlarmStates",
                column: "NodeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpcUaAlarmStates");
        }
    }
}
