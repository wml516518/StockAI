using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicalAndAnnouncementTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockAnnouncements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    PublishDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsNegative = table.Column<bool>(type: "INTEGER", nullable: false),
                    RiskKeywords = table.Column<string>(type: "TEXT", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAnnouncements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockTechnicals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    TradeDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MA5 = table.Column<decimal>(type: "TEXT", nullable: true),
                    MA10 = table.Column<decimal>(type: "TEXT", nullable: true),
                    MA20 = table.Column<decimal>(type: "TEXT", nullable: true),
                    MA60 = table.Column<decimal>(type: "TEXT", nullable: true),
                    MACD_DIF = table.Column<decimal>(type: "TEXT", nullable: true),
                    MACD_DEA = table.Column<decimal>(type: "TEXT", nullable: true),
                    MACD_HIST = table.Column<decimal>(type: "TEXT", nullable: true),
                    KDJ_K = table.Column<decimal>(type: "TEXT", nullable: true),
                    KDJ_D = table.Column<decimal>(type: "TEXT", nullable: true),
                    KDJ_J = table.Column<decimal>(type: "TEXT", nullable: true),
                    ATR = table.Column<decimal>(type: "TEXT", nullable: true),
                    Volume5DayAvg = table.Column<decimal>(type: "TEXT", nullable: true),
                    UpdateTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTechnicals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockAnnouncements_IsNegative",
                table: "StockAnnouncements",
                column: "IsNegative");

            migrationBuilder.CreateIndex(
                name: "IX_StockAnnouncements_StockCode_PublishDate",
                table: "StockAnnouncements",
                columns: new[] { "StockCode", "PublishDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTechnicals_StockCode_TradeDate",
                table: "StockTechnicals",
                columns: new[] { "StockCode", "TradeDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockAnnouncements");

            migrationBuilder.DropTable(
                name: "StockTechnicals");
        }
    }
}
