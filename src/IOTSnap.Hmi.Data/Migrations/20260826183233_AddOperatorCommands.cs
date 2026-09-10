using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IOTSnap.Hmi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperatorCommands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CommandId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorUsername = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorRole = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ScreenSlug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WidgetKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BindingRole = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RequestedValue = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RequiresConfirmation = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OpcUaResponse = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedValue = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RequestedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AcceptedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ObservedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatorCommands", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperatorCommands_ActorUsername_IdempotencyKey",
                table: "OperatorCommands",
                columns: new[] { "ActorUsername", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatorCommands_CommandId",
                table: "OperatorCommands",
                column: "CommandId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperatorCommands");
        }
    }
}
