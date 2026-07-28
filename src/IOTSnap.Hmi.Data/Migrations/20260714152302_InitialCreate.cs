using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastLoginUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpcUaConnectionProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EndpointUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseSecurity = table.Column<bool>(type: "INTEGER", nullable: false),
                    SecurityPolicy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SecurityMode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AuthenticationMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    PublishingIntervalMs = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpcUaConnectionProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpcUaNodeMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpcUaConnectionProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SamplingIntervalMs = table.Column<int>(type: "INTEGER", nullable: false),
                    IsWritable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Area = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpcUaNodeMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpcUaNodeMappings_OpcUaConnectionProfiles_OpcUaConnectionProfileId",
                        column: x => x.OpcUaConnectionProfileId,
                        principalTable: "OpcUaConnectionProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocalUsers_Username",
                table: "LocalUsers",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpcUaNodeMappings_OpcUaConnectionProfileId",
                table: "OpcUaNodeMappings",
                column: "OpcUaConnectionProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "LocalUsers");

            migrationBuilder.DropTable(
                name: "OpcUaNodeMappings");

            migrationBuilder.DropTable(
                name: "OpcUaConnectionProfiles");
        }
    }
}
