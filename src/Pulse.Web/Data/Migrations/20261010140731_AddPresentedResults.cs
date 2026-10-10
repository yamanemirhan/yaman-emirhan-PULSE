using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pulse.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPresentedResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PresentedResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ThroughSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    ServedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PresentedResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PresentedResults_QuestionId_ThroughSequence_AlgorithmVersion",
                table: "PresentedResults",
                columns: new[] { "QuestionId", "ThroughSequence", "AlgorithmVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PresentedResults");
        }
    }
}
