using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WebApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentEpisodeLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create AssessmentEpisodeLinks table
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

            // Migrate existing data: Convert EpisodeIds JSON array to AssessmentEpisodeLink records
            // For each assessment with EpisodeIds, create links with evenly distributed weights
            migrationBuilder.Sql(@"
                INSERT INTO conuhacks.""AssessmentEpisodeLinks"" (""AssessmentId"", ""EpisodeId"", ""Weight"", ""Reasoning"")
                SELECT 
                    a.""Id"" AS ""AssessmentId"",
                    jsonb_array_elements_text(a.""EpisodeIds"")::integer AS ""EpisodeId"",
                    CASE 
                        WHEN jsonb_array_length(a.""EpisodeIds"") > 0 
                        THEN 1.0 / jsonb_array_length(a.""EpisodeIds"")
                        ELSE 1.0 
                    END AS ""Weight"",
                    NULL AS ""Reasoning""
                FROM conuhacks.""Assessments"" a
                WHERE a.""EpisodeIds"" IS NOT NULL 
                  AND jsonb_array_length(a.""EpisodeIds"") > 0
                  AND EXISTS (
                      SELECT 1 FROM conuhacks.""Episodes"" e 
                      WHERE e.""Id"" = jsonb_array_elements_text(a.""EpisodeIds"")::integer
                  );
            ");

            // Drop EpisodeIds column
            migrationBuilder.DropColumn(
                name: "EpisodeIds",
                schema: "conuhacks",
                table: "Assessments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add EpisodeIds column
            migrationBuilder.AddColumn<string>(
                name: "EpisodeIds",
                schema: "conuhacks",
                table: "Assessments",
                type: "jsonb",
                nullable: true);

            // Migrate data back: Convert AssessmentEpisodeLinks to EpisodeIds JSON array
            migrationBuilder.Sql(@"
                UPDATE conuhacks.""Assessments"" a
                SET ""EpisodeIds"" = (
                    SELECT jsonb_agg(CAST(""EpisodeId"" AS text)::jsonb)
                    FROM conuhacks.""AssessmentEpisodeLinks"" ael
                    WHERE ael.""AssessmentId"" = a.""Id""
                )
                WHERE EXISTS (
                    SELECT 1 FROM conuhacks.""AssessmentEpisodeLinks"" ael2
                    WHERE ael2.""AssessmentId"" = a.""Id""
                );
            ");

            // Drop AssessmentEpisodeLinks table
            migrationBuilder.DropTable(
                name: "AssessmentEpisodeLinks",
                schema: "conuhacks");
        }
    }
}
