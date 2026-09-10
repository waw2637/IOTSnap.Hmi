using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpcUaAlarmTransitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpcUaAlarmTransitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Transition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpcUaAlarmTransitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpcUaAlarmTransitions_NodeId_OccurredUtc",
                table: "OpcUaAlarmTransitions",
                columns: new[] { "NodeId", "OccurredUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpcUaAlarmTransitions");
        }
    }
}
