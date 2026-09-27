using Carteira.Core;
using Microsoft.EntityFrameworkCore;

namespace Carteira.Web;

public class CarteiraDbContext(DbContextOptions<CarteiraDbContext> options) : DbContext(options)
{
    public DbSet<Titular> Titulares => Set<Titular>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<DailyPrice> DailyPrices => Set<DailyPrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Titular>(e =>
        {
            e.ToTable("titulares");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.Nome).HasColumnName("nome").IsRequired();
            e.Property(t => t.Slug).HasColumnName("slug").IsRequired();
            e.HasIndex(t => t.Slug).IsUnique();
        });

        modelBuilder.Entity<Asset>(e =>
        {
            e.ToTable("assets");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.Classe).HasColumnName("classe").HasConversion<string>().IsRequired();
            e.Property(a => a.Codigo).HasColumnName("code").IsRequired();
            e.Property(a => a.Nome).HasColumnName("nome").IsRequired();
            e.Property(a => a.Vencimento).HasColumnName("vencimento").IsRequired();
            e.HasIndex(a => a.Codigo).IsUnique();
        });

        modelBuilder.Entity<Trade>(e =>
        {
            e.ToTable("trades");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.TitularId).HasColumnName("titular_id");
            e.Property(t => t.AssetId).HasColumnName("asset_id");
            e.Property(t => t.Data).HasColumnName("data");
            e.Property(t => t.Tipo).HasColumnName("tipo").HasConversion<string>().IsRequired();
            e.Property(t => t.Quantidade).HasColumnName("quantidade").HasColumnType("decimal(18,8)");
            e.Property(t => t.PrecoUnitario).HasColumnName("preco_unitario").HasColumnType("decimal(18,6)");
            e.Property(t => t.Taxas).HasColumnName("taxas").HasColumnType("decimal(18,2)");
            e.Property(t => t.Moeda).HasColumnName("moeda").HasMaxLength(3).IsRequired();
            e.Property(t => t.ChaveImportacao).HasColumnName("import_key").IsRequired();
            e.HasIndex(t => t.ChaveImportacao).IsUnique();
        });

        modelBuilder.Entity<DailyPrice>(e =>
        {
            e.ToTable("daily_prices");
            e.HasKey(p => new { p.AssetId, p.Data });
            e.Property(p => p.AssetId).HasColumnName("asset_id");
            e.Property(p => p.Data).HasColumnName("date");
            e.Property(p => p.PrecoUnitario).HasColumnName("preco_unitario").HasColumnType("decimal(18,6)");
        });
    }
}
