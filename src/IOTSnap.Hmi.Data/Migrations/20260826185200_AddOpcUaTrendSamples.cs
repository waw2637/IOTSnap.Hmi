using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpcUaTrendSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpcUaTrendSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ValueText = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    StatusCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SampledUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpcUaTrendSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpcUaTrendSamples_NodeId_SampledUtc",
                table: "OpcUaTrendSamples",
                columns: new[] { "NodeId", "SampledUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpcUaTrendSamples");
        }
    }
}
