using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carteira.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDailySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_snapshots",
                columns: table => new
                {
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    titular_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custo = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    custo_com_preco = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    rentabilidade = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    resultado_realizado = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tem_posicao_sem_preco = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_snapshots", x => new { x.date, x.titular_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_snapshots_titular_id_date",
                table: "daily_snapshots",
                columns: new[] { "titular_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_snapshots");
        }
    }
}
