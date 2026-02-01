using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WebApi.Migrations
{
    /// <inheritdoc />
    public partial class Initial_20260201143426 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Frequency",
                schema: "conuhacks",
                table: "Symptoms");

            migrationBuilder.DropColumn(
                name: "OnsetDate",
                schema: "conuhacks",
                table: "Symptoms");

            migrationBuilder.DropColumn(
                name: "Severity",
                schema: "conuhacks",
                table: "Symptoms");

            migrationBuilder.DropColumn(
                name: "Triggers",
                schema: "conuhacks",
                table: "Symptoms");

            migrationBuilder.CreateTable(
                name: "Assessments",
                schema: "conuhacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Hypothesis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    Differentials = table.Column<string>(type: "jsonb", nullable: true),
                    Reasoning = table.Column<string>(type: "text", nullable: false),
                    RecommendedAction = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EpisodeIds = table.Column<string>(type: "jsonb", nullable: true),
                    NegativeFindingIds = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assessments_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "conuhacks",
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assessments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "conuhacks",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                schema: "conuhacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SymptomId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Triggers = table.Column<string>(type: "jsonb", nullable: true),
                    Relievers = table.Column<string>(type: "jsonb", nullable: true),
                    Pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Timeline = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_Symptoms_SymptomId",
                        column: x => x.SymptomId,
                        principalSchema: "conuhacks",
                        principalTable: "Symptoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Episodes_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "conuhacks",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NegativeFindings",
                schema: "conuhacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpisodeId = table.Column<int>(type: "integer", nullable: true),
                    SymptomName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NegativeFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NegativeFindings_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalSchema: "conuhacks",
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NegativeFindings_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "conuhacks",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_ConversationId",
                schema: "conuhacks",
                table: "Assessments",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_UserId",
                schema: "conuhacks",
                table: "Assessments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_StartedAt",
                schema: "conuhacks",
                table: "Episodes",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_Status",
                schema: "conuhacks",
                table: "Episodes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SymptomId",
                schema: "conuhacks",
                table: "Episodes",
                column: "SymptomId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_UserId",
                schema: "conuhacks",
                table: "Episodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeFindings_EpisodeId",
                schema: "conuhacks",
                table: "NegativeFindings",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_NegativeFindings_UserId",
                schema: "conuhacks",
                table: "NegativeFindings",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assessments",
                schema: "conuhacks");

            migrationBuilder.DropTable(
                name: "NegativeFindings",
                schema: "conuhacks");

            migrationBuilder.DropTable(
                name: "Episodes",
                schema: "conuhacks");

            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                schema: "conuhacks",
                table: "Symptoms",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnsetDate",
                schema: "conuhacks",
                table: "Symptoms",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Severity",
                schema: "conuhacks",
                table: "Symptoms",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Triggers",
                schema: "conuhacks",
                table: "Symptoms",
                type: "jsonb",
                nullable: true);
        }
    }
}
