using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gameya.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin is a PostgreSQL system column; nothing to add. The model snapshot records the token.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
