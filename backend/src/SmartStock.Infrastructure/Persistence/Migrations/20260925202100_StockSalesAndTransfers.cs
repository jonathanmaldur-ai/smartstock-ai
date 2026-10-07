using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockSalesAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "period_end",
                table: "import_batches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "period_start",
                table: "import_batches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "reference_date",
                table: "import_batches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "replaced_records",
                table: "import_batches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "store_id",
                table: "import_batches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_totals",
                columns: table => new
                {
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    store_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_totals", x => new { x.import_id, x.product_id });
                    table.ForeignKey(
                        name: "fk_sales_totals_import_batches_import_id",
                        column: x => x.import_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_totals_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sales_totals_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_levels",
                columns: table => new
                {
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    store_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_levels", x => new { x.import_id, x.product_id, x.store_id });
                    table.ForeignKey(
                        name: "fk_stock_levels_import_batches_import_id",
                        column: x => x.import_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_levels_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_levels_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_movements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    origin_store_id = table.Column<int>(type: "integer", nullable: false),
                    destination_store_id = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_cancellation = table.Column<bool>(type: "boolean", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    user_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    source_row = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_movements_import_batches_import_id",
                        column: x => x.import_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfer_movements_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfer_movements_stores_destination_store_id",
                        column: x => x.destination_store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfer_movements_stores_origin_store_id",
                        column: x => x.origin_store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_store_id",
                table: "import_batches",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_type_status_reference_date",
                table: "import_batches",
                columns: new[] { "type", "status", "reference_date" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_totals_product_id",
                table: "sales_totals",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_totals_store_id",
                table: "sales_totals",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_product_id",
                table: "stock_levels",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_store_id",
                table: "stock_levels",
                column: "store_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_movements_date",
                table: "transfer_movements",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_movements_destination_store_id",
                table: "transfer_movements",
                column: "destination_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_movements_import_id",
                table: "transfer_movements",
                column: "import_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_movements_origin_store_id",
                table: "transfer_movements",
                column: "origin_store_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_movements_product_id_date",
                table: "transfer_movements",
                columns: new[] { "product_id", "date" });

            migrationBuilder.AddForeignKey(
                name: "fk_import_batches_stores_store_id",
                table: "import_batches",
                column: "store_id",
                principalTable: "stores",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_import_batches_stores_store_id",
                table: "import_batches");

            migrationBuilder.DropTable(
                name: "sales_totals");

            migrationBuilder.DropTable(
                name: "stock_levels");

            migrationBuilder.DropTable(
                name: "transfer_movements");

            migrationBuilder.DropIndex(
                name: "ix_import_batches_store_id",
                table: "import_batches");

            migrationBuilder.DropIndex(
                name: "ix_import_batches_type_status_reference_date",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "period_end",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "period_start",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "reference_date",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "replaced_records",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "store_id",
                table: "import_batches");
        }
    }
}
