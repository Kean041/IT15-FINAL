using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinSight.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No schema changes are required; this migration reconciles the model snapshot.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No rollback is required; this migration only updates the model snapshot.
        }
    }
}
