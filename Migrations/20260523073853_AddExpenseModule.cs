using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSight.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Expenses",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<int>(
                name: "BudgetRequestID",
                table: "Expenses",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Expenses",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpenseDate",
                table: "Expenses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "ExpenseTitle",
                table: "Expenses",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Expenses",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('Expenses', 'CreatedBy') IS NULL
                BEGIN
                    DECLARE @DefaultExpenseUserID INT;
                    SELECT TOP 1 @DefaultExpenseUserID = UserID
                    FROM Users
                    ORDER BY CASE WHEN Email = 'superadmin@system.com' THEN 0 ELSE 1 END, UserID;

                    ALTER TABLE Expenses ADD CreatedBy INT NOT NULL DEFAULT (0);

                    IF @DefaultExpenseUserID IS NOT NULL
                        UPDATE Expenses SET CreatedBy = @DefaultExpenseUserID WHERE CreatedBy = 0;
                END

                IF COL_LENGTH('Expenses', 'Year') IS NULL
                BEGIN
                    ALTER TABLE Expenses ADD [Year] INT NOT NULL DEFAULT (YEAR(GETDATE()));

                    IF COL_LENGTH('Expenses', 'ExpenseDate') IS NOT NULL
                        UPDATE Expenses SET [Year] = YEAR(ExpenseDate);
                END");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BudgetRequestID",
                table: "Expenses",
                column: "BudgetRequestID");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_BudgetRequests_BudgetRequestID",
                table: "Expenses",
                column: "BudgetRequestID",
                principalTable: "BudgetRequests",
                principalColumn: "RequestID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_BudgetRequests_BudgetRequestID",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_BudgetRequestID",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "BudgetRequestID",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ExpenseDate",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ExpenseTitle",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Expenses");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Expenses",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);
        }
    }
}
