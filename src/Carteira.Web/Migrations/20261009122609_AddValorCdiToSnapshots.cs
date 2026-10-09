using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carteira.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddValorCdiToSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "valor_cdi",
                table: "daily_snapshots",
                type: "numeric(18,6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "valor_cdi",
                table: "daily_snapshots");
        }
    }
}
