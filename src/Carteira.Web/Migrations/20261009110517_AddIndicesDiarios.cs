using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carteira.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddIndicesDiarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "indices_diarios",
                columns: table => new
                {
                    indice = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indices_diarios", x => new { x.indice, x.date });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indices_diarios");
        }
    }
}
