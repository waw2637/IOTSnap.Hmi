using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHmiScreenPublications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HmiScreenPublications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HmiScreenId = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    PublishedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PublishedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HmiScreenPublications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HmiScreenPublications_HmiScreenId_PublishedUtc",
                table: "HmiScreenPublications",
                columns: new[] { "HmiScreenId", "PublishedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HmiScreenPublications");
        }
    }
}
