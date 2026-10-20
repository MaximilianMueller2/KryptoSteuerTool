using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KryptoSteuer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OptionsJson",
                table: "ImportRuns",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OptionsJson",
                table: "ImportRuns");
        }
    }
}
