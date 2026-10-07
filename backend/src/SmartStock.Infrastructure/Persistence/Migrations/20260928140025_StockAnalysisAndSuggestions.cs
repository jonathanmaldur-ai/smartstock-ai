using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockAnalysisAndSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_analyses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_date = table.Column<DateOnly>(type: "date", nullable: false),
                    analysis_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    position_count = table.Column<int>(type: "integer", nullable: false),
                    suggestion_count = table.Column<int>(type: "integer", nullable: false),
                    suggested_units = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    purchase_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_analyses", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_analyses_import_batches_stock_import_id",
                        column: x => x.stock_import_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_parameters",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    minimum_days = table.Column<int>(type: "integer", nullable: false),
                    ideal_days = table.Column<int>(type: "integer", nullable: false),
                    maximum_days = table.Column<int>(type: "integer", nullable: false),
                    excess_days = table.Column<int>(type: "integer", nullable: false),
                    critical_coverage_days = table.Column<int>(type: "integer", nullable: false),
                    minimum_annual_sales = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_parameters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_suggestions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    store_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    stock = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    daily_average = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false),
                    coverage_days = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_suggestions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_suggestions_stock_analyses_analysis_id",
                        column: x => x.analysis_id,
                        principalTable: "stock_analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_purchase_suggestions_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_positions",
                columns: table => new
                {
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    store_id = table.Column<int>(type: "integer", nullable: false),
                    stock = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    projected_stock = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    sold12months = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    daily_average = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false),
                    coverage_days = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    situation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_positions", x => new { x.analysis_id, x.product_id, x.store_id });
                    table.ForeignKey(
                        name: "fk_stock_positions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_positions_stock_analyses_analysis_id",
                        column: x => x.analysis_id,
                        principalTable: "stock_analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_stock_positions_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_suggestions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    origin_store_id = table.Column<int>(type: "integer", nullable: false),
                    destination_store_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    origin_stock = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    origin_daily_average = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false),
                    origin_coverage_days = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    destination_stock = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    destination_daily_average = table.Column<decimal>(type: "numeric(14,6)", precision: 14, scale: 6, nullable: false),
                    destination_coverage_days = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    destination_coverage_after = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    destination_negative = table.Column<bool>(type: "boolean", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_suggestions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfer_suggestions_stock_analyses_analysis_id",
                        column: x => x.analysis_id,
                        principalTable: "stock_analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfer_suggestions_stores_destination_store_id",
                        column: x => x.destination_store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfer_suggestions_stores_origin_store_id",
                        column: x => x.origin_store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "stock_parameters",
                columns: new[] { "id", "critical_coverage_days", "excess_days", "ideal_days", "maximum_days", "minimum_annual_sales", "minimum_days", "updated_at", "updated_by_email" },
                values: new object[] { 1, 7, 120, 30, 60, 12m, 15, new DateTimeOffset(new DateTime(2026, 9, 28, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "sistema" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_suggestions_analysis_id",
                table: "purchase_suggestions",
                column: "analysis_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_suggestions_product_id",
                table: "purchase_suggestions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_suggestions_store_id",
                table: "purchase_suggestions",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_analyses_created_at",
                table: "stock_analyses",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_stock_analyses_stock_import_id",
                table: "stock_analyses",
                column: "stock_import_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_positions_analysis_id_situation",
                table: "stock_positions",
                columns: new[] { "analysis_id", "situation" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_positions_product_id",
                table: "stock_positions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_positions_store_id",
                table: "stock_positions",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_suggestions_analysis_id",
                table: "transfer_suggestions",
                column: "analysis_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_suggestions_destination_store_id",
                table: "transfer_suggestions",
                column: "destination_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_suggestions_origin_store_id",
                table: "transfer_suggestions",
                column: "origin_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_suggestions_product_id_status",
                table: "transfer_suggestions",
                columns: new[] { "product_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_suggestions_status_analysis_id",
                table: "transfer_suggestions",
                columns: new[] { "status", "analysis_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_suggestions");

            migrationBuilder.DropTable(
                name: "stock_parameters");

            migrationBuilder.DropTable(
                name: "stock_positions");

            migrationBuilder.DropTable(
                name: "transfer_suggestions");

            migrationBuilder.DropTable(
                name: "stock_analyses");
        }
    }
}
