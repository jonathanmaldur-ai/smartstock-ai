using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartStock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuggestionCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "completed_on",
                table: "transfer_suggestions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "completed_quantity",
                table: "transfer_suggestions",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_on",
                table: "transfer_suggestions");

            migrationBuilder.DropColumn(
                name: "completed_quantity",
                table: "transfer_suggestions");
        }
    }
}
