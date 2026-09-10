using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorAuditEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperatorAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorRole = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Resource = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    OccurredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorAuditEntries_OccurredUtc",
                table: "OperatorAuditEntries",
                column: "OccurredUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperatorAuditEntries");
        }
    }
}
