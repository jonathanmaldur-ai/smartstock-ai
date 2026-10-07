using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NegativeStockPanel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "negative_stocks",
                columns: table => new
                {
                    analysis_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    store_id = table.Column<int>(type: "integer", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    sold12months = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    causes = table.Column<int>(type: "integer", nullable: false),
                    pending_transfer_units = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    duplicate_product_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negative_stocks", x => new { x.analysis_id, x.product_id, x.store_id });
                    table.ForeignKey(
                        name: "fk_negative_stocks_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_negative_stocks_stock_analyses_analysis_id",
                        column: x => x.analysis_id,
                        principalTable: "stock_analyses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_negative_stocks_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_negative_stocks_analysis_id_store_id",
                table: "negative_stocks",
                columns: new[] { "analysis_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_negative_stocks_product_id",
                table: "negative_stocks",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_negative_stocks_store_id",
                table: "negative_stocks",
                column: "store_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "negative_stocks");
        }
    }
}
