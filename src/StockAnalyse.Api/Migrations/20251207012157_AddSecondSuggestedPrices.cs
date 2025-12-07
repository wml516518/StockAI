using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSecondSuggestedPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedBuyPrice2",
                table: "WatchlistStocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedSellPrice2",
                table: "WatchlistStocks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestedBuyPrice2",
                table: "WatchlistStocks");

            migrationBuilder.DropColumn(
                name: "SuggestedSellPrice2",
                table: "WatchlistStocks");
        }
    }
}
