using Carteira.Core;
using Microsoft.EntityFrameworkCore;

namespace Carteira.Web;

public class CarteiraDbContext(DbContextOptions<CarteiraDbContext> options) : DbContext(options)
{
    public DbSet<Titular> Titulares => Set<Titular>();
    public DbSet<Ativo> Ativos => Set<Ativo>();
    public DbSet<Operacao> Operacoes => Set<Operacao>();
    public DbSet<PrecoDiario> PrecosDiarios => Set<PrecoDiario>();
    public DbSet<SnapshotDiario> Snapshots => Set<SnapshotDiario>();

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

        modelBuilder.Entity<Ativo>(e =>
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

        modelBuilder.Entity<Operacao>(e =>
        {
            e.ToTable("trades");
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasColumnName("id");
            e.Property(o => o.TitularId).HasColumnName("titular_id");
            e.Property(o => o.AtivoId).HasColumnName("asset_id");
            e.Property(o => o.Data).HasColumnName("data");
            e.Property(o => o.Tipo).HasColumnName("tipo").HasConversion<string>().IsRequired();
            e.Property(o => o.Quantidade).HasColumnName("quantidade").HasColumnType("decimal(18,8)");
            e.Property(o => o.PrecoUnitario).HasColumnName("preco_unitario").HasColumnType("decimal(18,6)");
            e.Property(o => o.Taxas).HasColumnName("taxas").HasColumnType("decimal(18,2)");
            e.Property(o => o.Moeda).HasColumnName("moeda").HasMaxLength(3).IsRequired();
            e.Property(o => o.ChaveImportacao).HasColumnName("import_key").IsRequired();
            e.HasIndex(o => o.ChaveImportacao).IsUnique();
        });

        modelBuilder.Entity<PrecoDiario>(e =>
        {
            e.ToTable("daily_prices");
            e.HasKey(p => new { p.AtivoId, p.Data });
            e.Property(p => p.AtivoId).HasColumnName("asset_id");
            e.Property(p => p.Data).HasColumnName("date");
            e.Property(p => p.PrecoUnitario).HasColumnName("preco_unitario").HasColumnType("decimal(18,6)");
        });

        modelBuilder.Entity<SnapshotDiario>(e =>
        {
            e.ToTable("daily_snapshots");
            e.HasKey(s => new { s.Data, s.TitularId });
            e.Property(s => s.Data).HasColumnName("date");
            e.Property(s => s.TitularId).HasColumnName("titular_id");
            e.Property(s => s.Custo).HasColumnName("custo").HasColumnType("decimal(18,6)");
            e.Property(s => s.CustoComPreco).HasColumnName("custo_com_preco").HasColumnType("decimal(18,6)");
            e.Property(s => s.Valor).HasColumnName("valor").HasColumnType("decimal(18,6)");
            e.Property(s => s.Rentabilidade).HasColumnName("rentabilidade").HasColumnType("decimal(18,6)");
            e.Property(s => s.ResultadoRealizado).HasColumnName("resultado_realizado").HasColumnType("decimal(18,6)");
            e.Property(s => s.TemPosicaoSemPreco).HasColumnName("tem_posicao_sem_preco");
            e.HasIndex(s => new { s.TitularId, s.Data });
        });
    }
}
