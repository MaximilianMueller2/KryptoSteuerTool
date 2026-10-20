using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KryptoSteuer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "RawRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LineNumber",
                table: "RawRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CommittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReportJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportRuns_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawRecords_Source_ContentHash",
                table: "RawRecords",
                columns: new[] { "Source", "ContentHash" },
                unique: true,
                filter: "\"ExternalId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_AccountId",
                table: "ImportRuns",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_FileHash",
                table: "ImportRuns",
                column: "FileHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportRuns");

            migrationBuilder.DropIndex(
                name: "IX_RawRecords_Source_ContentHash",
                table: "RawRecords");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "RawRecords");

            migrationBuilder.DropColumn(
                name: "LineNumber",
                table: "RawRecords");
        }
    }
}
