using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DailySales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_sales",
                columns: table => new
                {
                    store_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sales = table.Column<int>(type: "integer", nullable: false),
                    pieces = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    gross = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    net = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_sales", x => new { x.store_id, x.date });
                    table.ForeignKey(
                        name: "fk_daily_sales_import_batches_import_id",
                        column: x => x.import_id,
                        principalTable: "import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_daily_sales_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_daily_sales_date",
                table: "daily_sales",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_daily_sales_import_id",
                table: "daily_sales",
                column: "import_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_sales");
        }
    }
}
