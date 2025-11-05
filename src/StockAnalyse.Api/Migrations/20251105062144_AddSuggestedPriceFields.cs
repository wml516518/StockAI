using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSuggestedPriceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BuyAlertSent",
                table: "WatchlistStocks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SellAlertSent",
                table: "WatchlistStocks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedBuyPrice",
                table: "WatchlistStocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedSellPrice",
                table: "WatchlistStocks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyAlertSent",
                table: "WatchlistStocks");

            migrationBuilder.DropColumn(
                name: "SellAlertSent",
                table: "WatchlistStocks");

            migrationBuilder.DropColumn(
                name: "SuggestedBuyPrice",
                table: "WatchlistStocks");

            migrationBuilder.DropColumn(
                name: "SuggestedSellPrice",
                table: "WatchlistStocks");
        }
    }
}
