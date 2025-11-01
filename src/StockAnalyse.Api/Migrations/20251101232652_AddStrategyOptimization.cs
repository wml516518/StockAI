using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalyse.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyOptimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StrategyOptimizationResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StrategyId = table.Column<int>(type: "INTEGER", nullable: false),
                    OptimizedParameters = table.Column<string>(type: "TEXT", nullable: false),
                    OptimizationConfig = table.Column<string>(type: "TEXT", nullable: false),
                    TotalReturn = table.Column<decimal>(type: "TEXT", nullable: false),
                    SharpeRatio = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "TEXT", nullable: false),
                    WinRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalTrades = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalCombinations = table.Column<int>(type: "INTEGER", nullable: false),
                    TestedCombinations = table.Column<int>(type: "INTEGER", nullable: false),
                    OptimizationDuration = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StockCodes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsApplied = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyOptimizationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyOptimizationResults_QuantStrategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "QuantStrategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParameterTestResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OptimizationResultId = table.Column<int>(type: "INTEGER", nullable: false),
                    Parameters = table.Column<string>(type: "TEXT", nullable: false),
                    TotalReturn = table.Column<decimal>(type: "TEXT", nullable: false),
                    SharpeRatio = table.Column<decimal>(type: "TEXT", nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "TEXT", nullable: false),
                    WinRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalTrades = table.Column<int>(type: "INTEGER", nullable: false),
                    TestedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParameterTestResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParameterTestResults_StrategyOptimizationResults_OptimizationResultId",
                        column: x => x.OptimizationResultId,
                        principalTable: "StrategyOptimizationResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParameterTestResults_OptimizationResultId",
                table: "ParameterTestResults",
                column: "OptimizationResultId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyOptimizationResults_StrategyId",
                table: "StrategyOptimizationResults",
                column: "StrategyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParameterTestResults");

            migrationBuilder.DropTable(
                name: "StrategyOptimizationResults");
        }
    }
}
