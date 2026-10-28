using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Seasonality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "season_id",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    start_month = table.Column<int>(type: "integer", nullable: false),
                    end_month = table.Column<int>(type: "integer", nullable: false),
                    world_cup_only = table.Column<bool>(type: "boolean", nullable: false),
                    keywords = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasons", x => x.id);
                    table.CheckConstraint("ck_seasons_months", "start_month BETWEEN 1 AND 12 AND end_month BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "seasonal_suggestions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    season_id = table.Column<int>(type: "integer", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasonal_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_seasonal_suggestions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seasonal_suggestions_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_season_id",
                table: "products",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ix_seasonal_suggestions_product_id",
                table: "seasonal_suggestions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_seasonal_suggestions_season_id",
                table: "seasonal_suggestions",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "ix_seasonal_suggestions_status_season_id",
                table: "seasonal_suggestions",
                columns: new[] { "status", "season_id" });

            migrationBuilder.CreateIndex(
                name: "ix_seasons_name",
                table: "seasons",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_products_seasons_season_id",
                table: "products",
                column: "season_id",
                principalTable: "seasons",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_products_seasons_season_id",
                table: "products");

            migrationBuilder.DropTable(
                name: "seasonal_suggestions");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropIndex(
                name: "ix_products_season_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "season_id",
                table: "products");
        }
    }
}
