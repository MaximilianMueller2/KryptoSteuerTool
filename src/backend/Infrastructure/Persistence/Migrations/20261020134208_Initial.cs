using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KryptoSteuer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Platform = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    SubAccount = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Class = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    ContractAddress = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Chain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsTauschkryptowert = table.Column<bool>(type: "boolean", nullable: false),
                    IsWrappedOf = table.Column<bool>(type: "boolean", nullable: false),
                    WrappedOfAssetId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RawRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ImportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TaxYear = table.Column<int>(type: "integer", nullable: false),
                    RuleSetVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultSnapshotJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    RawRecordIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Prices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EurPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prices_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Lots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    AcquiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CostEur = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Regime = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lots_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lots_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lots_Transactions_SourceTransactionId",
                        column: x => x.SourceTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionLegs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsFee = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionLegs", x => x.Id);
                    table.CheckConstraint("ck_transaction_leg_amount_positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_TransactionLegs_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionLegs_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferOutTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferInTransactionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferLinks_Transactions_TransferInTransactionId",
                        column: x => x.TransferInTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferLinks_Transactions_TransferOutTransactionId",
                        column: x => x.TransferOutTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Disposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaxRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    ProceedsEur = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    CostEur = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    FeeShareEur = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    GainEur = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    HoldingDays = table.Column<int>(type: "integer", nullable: false),
                    IsTaxable = table.Column<bool>(type: "boolean", nullable: false),
                    RuleSetVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Disposals_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Disposals_TaxRuns_TaxRunId",
                        column: x => x.TaxRunId,
                        principalTable: "TaxRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Disposals_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Symbol",
                table: "Assets",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityType_EntityId",
                table: "AuditLog",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Disposals_LotId",
                table: "Disposals",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_Disposals_TaxRunId",
                table: "Disposals",
                column: "TaxRunId");

            migrationBuilder.CreateIndex(
                name: "IX_Disposals_TransactionId",
                table: "Disposals",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_AccountId_AssetId_AcquiredAtUtc",
                table: "Lots",
                columns: new[] { "AccountId", "AssetId", "AcquiredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Lots_AssetId",
                table: "Lots",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_SourceTransactionId",
                table: "Lots",
                column: "SourceTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Prices_AssetId_TimestampUtc_Source",
                table: "Prices",
                columns: new[] { "AssetId", "TimestampUtc", "Source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawRecords_ImportRunId",
                table: "RawRecords",
                column: "ImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_RawRecords_Source_ExternalId",
                table: "RawRecords",
                columns: new[] { "Source", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRuns_TaxYear_InputHash",
                table: "TaxRuns",
                columns: new[] { "TaxYear", "InputHash" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLegs_AssetId",
                table: "TransactionLegs",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionLegs_TransactionId",
                table: "TransactionLegs",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_TimestampUtc",
                table: "Transactions",
                columns: new[] { "AccountId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Source_ContentHash",
                table: "Transactions",
                columns: new[] { "Source", "ContentHash" },
                unique: true,
                filter: "\"ExternalId\" IS NULL AND \"ContentHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Source_ExternalId",
                table: "Transactions",
                columns: new[] { "Source", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransferLinks_TransferInTransactionId",
                table: "TransferLinks",
                column: "TransferInTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferLinks_TransferOutTransactionId",
                table: "TransferLinks",
                column: "TransferOutTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "Disposals");

            migrationBuilder.DropTable(
                name: "Prices");

            migrationBuilder.DropTable(
                name: "RawRecords");

            migrationBuilder.DropTable(
                name: "TransactionLegs");

            migrationBuilder.DropTable(
                name: "TransferLinks");

            migrationBuilder.DropTable(
                name: "Lots");

            migrationBuilder.DropTable(
                name: "TaxRuns");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
