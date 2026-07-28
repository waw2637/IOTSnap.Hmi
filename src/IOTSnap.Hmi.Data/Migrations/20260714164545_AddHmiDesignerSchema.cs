using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHmiDesignerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HmiScreens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HmiScreens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HmiWidgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HmiScreenId = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WidgetType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    X = table.Column<int>(type: "INTEGER", nullable: false),
                    Y = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    ZIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    PropertiesJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HmiWidgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HmiWidgets_HmiScreens_HmiScreenId",
                        column: x => x.HmiScreenId,
                        principalTable: "HmiScreens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HmiWidgetBindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HmiWidgetId = table.Column<int>(type: "INTEGER", nullable: false),
                    BindingRole = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    WriteRequiresConfirm = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinRole = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HmiWidgetBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HmiWidgetBindings_HmiWidgets_HmiWidgetId",
                        column: x => x.HmiWidgetId,
                        principalTable: "HmiWidgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HmiScreens_Slug",
                table: "HmiScreens",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HmiWidgetBindings_HmiWidgetId_BindingRole",
                table: "HmiWidgetBindings",
                columns: new[] { "HmiWidgetId", "BindingRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HmiWidgets_HmiScreenId_Key",
                table: "HmiWidgets",
                columns: new[] { "HmiScreenId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HmiWidgetBindings");

            migrationBuilder.DropTable(
                name: "HmiWidgets");

            migrationBuilder.DropTable(
                name: "HmiScreens");
        }
    }
}
