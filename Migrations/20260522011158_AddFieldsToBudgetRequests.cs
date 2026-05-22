using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSight.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldsToBudgetRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateNeeded",
                table: "BudgetRequests",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "BudgetRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "BudgetRequests",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "Budget Request");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateNeeded",
                table: "BudgetRequests");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "BudgetRequests");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "BudgetRequests");
        }
    }
}
