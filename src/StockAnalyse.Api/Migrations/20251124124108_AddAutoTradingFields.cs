using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoTradingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoTradingEnabled",
                table: "WatchlistStocks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AutoTradingIntervalMinutes",
                table: "WatchlistStocks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TradingPlan",
                table: "WatchlistStocks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TradingPlanUpdateTime",
                table: "WatchlistStocks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoTradingEnabled",
                table: "WatchlistStocks");

            migrationBuilder.DropColumn(
                name: "AutoTradingIntervalMinutes",
                table: "WatchlistStocks");

            migrationBuilder.DropColumn(
                name: "TradingPlan",
                table: "WatchlistStocks");

            migrationBuilder.DropColumn(
                name: "TradingPlanUpdateTime",
                table: "WatchlistStocks");
        }
    }
}
