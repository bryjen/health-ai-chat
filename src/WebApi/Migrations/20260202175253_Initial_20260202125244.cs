using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApi.Migrations
{
    /// <inheritdoc />
    public partial class Initial_20260202125244 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpisodeIds",
                schema: "conuhacks",
                table: "Assessments");

            migrationBuilder.CreateTable(
                name: "AssessmentEpisodeLinks",
                schema: "conuhacks",
                columns: table => new
                {
                    AssessmentId = table.Column<int>(type: "integer", nullable: false),
                    EpisodeId = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    Reasoning = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentEpisodeLinks", x => new { x.AssessmentId, x.EpisodeId });
                    table.ForeignKey(
                        name: "FK_AssessmentEpisodeLinks_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalSchema: "conuhacks",
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentEpisodeLinks_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "conuhacks",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentEpisodeLinks_AssessmentId",
                schema: "conuhacks",
                table: "AssessmentEpisodeLinks",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentEpisodeLinks_EpisodeId",
                schema: "conuhacks",
                table: "AssessmentEpisodeLinks",
                column: "EpisodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentEpisodeLinks",
                schema: "conuhacks");

            migrationBuilder.AddColumn<string>(
                name: "EpisodeIds",
                schema: "conuhacks",
                table: "Assessments",
                type: "jsonb",
                nullable: true);
        }
    }
}
