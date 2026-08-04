using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSight.Migrations
{
    /// <inheritdoc />
    public partial class AddScenarioEconomicAssumptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AppliedExchangeRate",
                table: "Scenarios",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AppliedInflation",
                table: "Scenarios",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliedExchangeRate",
                table: "Scenarios");

            migrationBuilder.DropColumn(
                name: "AppliedInflation",
                table: "Scenarios");
        }
    }
}
