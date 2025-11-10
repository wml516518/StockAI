using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockDataCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockDataCaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastRefreshedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Metadata = table.Column<string>(type: "TEXT", nullable: true),
                    IsFallbackData = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockDataCaches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockDataCaches_StockCode_DataType",
                table: "StockDataCaches",
                columns: new[] { "StockCode", "DataType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockDataCaches");
        }
    }
}
