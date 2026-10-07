using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sales",
                table: "daily_sale_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "store_sales",
                columns: table => new
                {
                    store_id = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gross = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    net = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    registered_customer = table.Column<bool>(type: "boolean", nullable: false),
                    payment = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    fiscal_document = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_sales", x => new { x.store_id, x.number });
                    table.ForeignKey(
                        name: "fk_store_sales_import_batches_import_id",
                        column: x => x.import_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_store_sales_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "store_sale_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    store_id = table.Column<int>(type: "integer", nullable: false),
                    sale_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_sale_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_sale_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_store_sale_lines_store_sales_store_id_sale_number",
                        columns: x => new { x.store_id, x.sale_number },
                        principalTable: "store_sales",
                        principalColumns: new[] { "store_id", "number" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_store_sale_lines_product_id",
                table: "store_sale_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_sale_lines_store_id_date_product_id",
                table: "store_sale_lines",
                columns: new[] { "store_id", "date", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_store_sale_lines_store_id_sale_number",
                table: "store_sale_lines",
                columns: new[] { "store_id", "sale_number" });

            migrationBuilder.CreateIndex(
                name: "ix_store_sales_import_id",
                table: "store_sales",
                column: "import_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_sales_store_id_date",
                table: "store_sales",
                columns: new[] { "store_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_sale_lines");

            migrationBuilder.DropTable(
                name: "store_sales");

            migrationBuilder.DropColumn(
                name: "sales",
                table: "daily_sale_items");
        }
    }
}
