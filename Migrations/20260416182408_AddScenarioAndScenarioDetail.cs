using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSight.Migrations
{
    /// <inheritdoc />
    public partial class AddScenarioAndScenarioDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    TenantID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.TenantID);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    DepartmentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TenantID = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.DepartmentID);
                    table.ForeignKey(
                        name: "FK_Departments_Tenants_TenantID",
                        column: x => x.TenantID,
                        principalTable: "Tenants",
                        principalColumn: "TenantID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RoleID = table.Column<int>(type: "int", nullable: true),
                    TenantID = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserID);
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantID",
                        column: x => x.TenantID,
                        principalTable: "Tenants",
                        principalColumn: "TenantID");
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    BudgetID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentID = table.Column<int>(type: "int", nullable: false),
                    TenantID = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.BudgetID);
                    table.ForeignKey(
                        name: "FK_Budgets_Departments_DepartmentID",
                        column: x => x.DepartmentID,
                        principalTable: "Departments",
                        principalColumn: "DepartmentID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Budgets_Tenants_TenantID",
                        column: x => x.TenantID,
                        principalTable: "Tenants",
                        principalColumn: "TenantID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Budgets_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Forecasts",
                columns: table => new
                {
                    ForecastID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentID = table.Column<int>(type: "int", nullable: false),
                    TenantID = table.Column<int>(type: "int", nullable: false),
                    BudgetID = table.Column<int>(type: "int", nullable: false),
                    ForecastType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PredictedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Forecasts", x => x.ForecastID);
                    table.ForeignKey(
                        name: "FK_Forecasts_Budgets_BudgetID",
                        column: x => x.BudgetID,
                        principalTable: "Budgets",
                        principalColumn: "BudgetID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Forecasts_Departments_DepartmentID",
                        column: x => x.DepartmentID,
                        principalTable: "Departments",
                        principalColumn: "DepartmentID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Forecasts_Tenants_TenantID",
                        column: x => x.TenantID,
                        principalTable: "Tenants",
                        principalColumn: "TenantID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Forecasts_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── NEW TABLES ────────────────────────────────────────────────────

            migrationBuilder.CreateTable(
                name: "Scenarios",
                columns: table => new
                {
                    ScenarioID   = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScenarioName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description  = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantID     = table.Column<int>(type: "int", nullable: false),
                    CreatedBy    = table.Column<int>(type: "int", nullable: false),
                    CreatedAt    = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenarios", x => x.ScenarioID);
                    table.ForeignKey(
                        name: "FK_Scenarios_Tenants_TenantID",
                        column: x => x.TenantID,
                        principalTable: "Tenants",
                        principalColumn: "TenantID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Scenarios_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "UserID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScenarioDetails",
                columns: table => new
                {
                    ScenarioDetailID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScenarioID     = table.Column<int>(type: "int", nullable: false),
                    BudgetID       = table.Column<int>(type: "int", nullable: false),
                    DepartmentID   = table.Column<int>(type: "int", nullable: false),
                    AdjustedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TenantID       = table.Column<int>(type: "int", nullable: false),
                    CreatedAt      = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioDetails", x => x.ScenarioDetailID);
                    table.ForeignKey(
                        name: "FK_ScenarioDetails_Scenarios_ScenarioID",
                        column: x => x.ScenarioID,
                        principalTable: "Scenarios",
                        principalColumn: "ScenarioID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScenarioDetails_Budgets_BudgetID",
                        column: x => x.BudgetID,
                        principalTable: "Budgets",
                        principalColumn: "BudgetID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScenarioDetails_Departments_DepartmentID",
                        column: x => x.DepartmentID,
                        principalTable: "Departments",
                        principalColumn: "DepartmentID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScenarioDetails_Tenants_TenantID",
                        column: x => x.TenantID,
                        principalTable: "Tenants",
                        principalColumn: "TenantID",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── Indexes ───────────────────────────────────────────────────────

            migrationBuilder.CreateIndex(name: "IX_Budgets_CreatedBy",      table: "Budgets",    column: "CreatedBy");
            migrationBuilder.CreateIndex(name: "IX_Budgets_DepartmentID",   table: "Budgets",    column: "DepartmentID");
            migrationBuilder.CreateIndex(name: "IX_Budgets_TenantID",       table: "Budgets",    column: "TenantID");
            migrationBuilder.CreateIndex(name: "IX_Departments_TenantID",   table: "Departments", column: "TenantID");
            migrationBuilder.CreateIndex(name: "IX_Forecasts_BudgetID",     table: "Forecasts",   column: "BudgetID");
            migrationBuilder.CreateIndex(name: "IX_Forecasts_CreatedBy",    table: "Forecasts",   column: "CreatedBy");
            migrationBuilder.CreateIndex(name: "IX_Forecasts_DepartmentID", table: "Forecasts",   column: "DepartmentID");
            migrationBuilder.CreateIndex(name: "IX_Forecasts_TenantID",     table: "Forecasts",   column: "TenantID");
            migrationBuilder.CreateIndex(name: "IX_Users_TenantID",         table: "Users",       column: "TenantID");
            migrationBuilder.CreateIndex(
                name: "IX_Users_Email", table: "Users", column: "Email", unique: true);

            migrationBuilder.CreateIndex(name: "IX_Scenarios_CreatedBy",           table: "Scenarios",      column: "CreatedBy");
            migrationBuilder.CreateIndex(name: "IX_Scenarios_TenantID",            table: "Scenarios",      column: "TenantID");
            migrationBuilder.CreateIndex(name: "IX_ScenarioDetails_ScenarioID",    table: "ScenarioDetails", column: "ScenarioID");
            migrationBuilder.CreateIndex(name: "IX_ScenarioDetails_BudgetID",      table: "ScenarioDetails", column: "BudgetID");
            migrationBuilder.CreateIndex(name: "IX_ScenarioDetails_DepartmentID",  table: "ScenarioDetails", column: "DepartmentID");
            migrationBuilder.CreateIndex(name: "IX_ScenarioDetails_TenantID",      table: "ScenarioDetails", column: "TenantID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ScenarioDetails");
            migrationBuilder.DropTable(name: "Scenarios");
            migrationBuilder.DropTable(name: "Forecasts");
            migrationBuilder.DropTable(name: "Budgets");
            migrationBuilder.DropTable(name: "Departments");
            migrationBuilder.DropTable(name: "Users");
            migrationBuilder.DropTable(name: "Tenants");
        }
    }
}
