using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pulse.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorRole = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    HashVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PreviousHash = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ActorId_QuestionId_RequestId",
                table: "Events",
                columns: new[] { "ActorId", "QuestionId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_QuestionId",
                table: "Events",
                column: "QuestionId",
                unique: true,
                filter: "\"Kind\" = 'QuestionCreated'");

            migrationBuilder.CreateIndex(
                name: "IX_Events_QuestionId_Sequence",
                table: "Events",
                columns: new[] { "QuestionId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
