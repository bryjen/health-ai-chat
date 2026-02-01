using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApi.Migrations
{
    /// <inheritdoc />
    public partial class Initial_20260129153913 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneCountryCode",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                schema: "conuhacks",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                schema: "conuhacks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "City",
                schema: "conuhacks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Country",
                schema: "conuhacks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FirstName",
                schema: "conuhacks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastName",
                schema: "conuhacks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneCountryCode",
                schema: "conuhacks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                schema: "conuhacks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                schema: "conuhacks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Username",
                schema: "conuhacks",
                table: "Users");
        }
    }
}
