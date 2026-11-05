using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Agente.Infrastructure.Persistencia.Postgres;

public static class OrigemTokens
{
    public const string Informado = "informado";
    public const string Desconhecido = "desconhecido";
}

public sealed class ConsumoIa
{
    public Guid Id { get; set; }
    public Guid ExecucaoId { get; set; }
    public required string CorrelacaoId { get; set; }
    public required string OrgId { get; set; }
    public required string Provedor { get; set; }
    public required string Modelo { get; set; }
    public required string Operacao { get; set; }
    public int Tentativa { get; set; }
    public int? TokensEntrada { get; set; }
    public int? TokensSaida { get; set; }
    public required string OrigemTokens { get; set; }
    public decimal? CustoEstimado { get; set; }
    public string? Moeda { get; set; }
    public Guid? TabelaPrecosId { get; set; }
    public DateTimeOffset Inicio { get; set; }
    public int DuracaoMs { get; set; }
    public required string Status { get; set; }
}

public sealed class TabelaPreco
{
    public Guid Id { get; set; }
    public required string Provedor { get; set; }
    public required string Modelo { get; set; }
    public decimal PrecoEntradaPorMilhao { get; set; }
    public decimal PrecoSaidaPorMilhao { get; set; }
    public required string Moeda { get; set; }
    public DateOnly VigenteDesde { get; set; }
    public required string Referencia { get; set; }
    public required string VersaoArquivo { get; set; }
}

public sealed class TabelaReferencia
{
    public Guid Id { get; set; }
    public required string Tipo { get; set; }
    public required string Origem { get; set; }
    public required string Versao { get; set; }
    public DateOnly VigenciaInicio { get; set; }
    public DateOnly? VigenciaFim { get; set; }
    public required string Sha256 { get; set; }
    public DateTimeOffset ImportadoEm { get; set; }
    public required string ImportadoPor { get; set; }
}

public sealed class Ncm
{
    public Guid Id { get; set; }
    public Guid TabelaReferenciaId { get; set; }
    public required string Codigo { get; set; }
    public required string Descricao { get; set; }
    public DateOnly? VigenciaInicio { get; set; }
    public DateOnly? VigenciaFim { get; set; }
}

public sealed partial class AgenteDbContext(DbContextOptions<AgenteDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<ConsumoIa> ConsumosIa => Set<ConsumoIa>();
    public DbSet<TabelaPreco> TabelasPrecos => Set<TabelaPreco>();
    public DbSet<TabelaReferencia> TabelasReferencia => Set<TabelaReferencia>();
    public DbSet<Ncm> Ncms => Set<Ncm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsumoIa>(e =>
        {
            e.ToTable("consumo_ia", t => t.HasCheckConstraint(
                "ck_consumo_ia_origem_tokens", "origem_tokens IN ('informado', 'desconhecido')"));
            e.Property(p => p.CustoEstimado).HasPrecision(18, 8);
            e.Property(p => p.Moeda).HasMaxLength(3).IsFixedLength();
            e.HasOne<TabelaPreco>().WithMany().HasForeignKey(p => p.TabelaPrecosId);
            e.HasIndex(p => p.ExecucaoId);
            e.HasIndex(p => new { p.OrgId, p.Inicio });
        });

        modelBuilder.Entity<TabelaPreco>(e =>
        {
            e.ToTable("tabela_precos");
            e.Property(p => p.PrecoEntradaPorMilhao).HasPrecision(18, 8);
            e.Property(p => p.PrecoSaidaPorMilhao).HasPrecision(18, 8);
            e.Property(p => p.Moeda).HasMaxLength(3).IsFixedLength();
            e.HasIndex(p => new { p.Provedor, p.Modelo, p.VigenteDesde }).IsUnique();
        });

        modelBuilder.Entity<TabelaReferencia>(e =>
        {
            e.ToTable("tabela_referencia");
            e.Property(p => p.Sha256).HasMaxLength(64).IsFixedLength();
            e.HasIndex(p => new { p.Tipo, p.Versao }).IsUnique();
        });

        modelBuilder.Entity<Ncm>(e =>
        {
            e.ToTable("ncm");
            e.Property(p => p.Codigo).HasMaxLength(8).IsFixedLength();
            e.HasOne<TabelaReferencia>().WithMany().HasForeignKey(p => p.TabelaReferenciaId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.TabelaReferenciaId, p.Codigo });
        });

        foreach (var entidade in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var propriedade in entidade.GetProperties())
            {
                propriedade.SetColumnName(ParaSnakeCase(propriedade.Name));
            }
        }
    }

    private static string ParaSnakeCase(string nome) =>
        string.Concat(SnakeCase().Replace(nome, "$1_$2").Select(char.ToLowerInvariant));

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SnakeCase();
}

/// <summary>Usado só por dotnet ef; a geração de migrações não abre conexão.</summary>
public sealed class AgenteDbContextFactory : IDesignTimeDbContextFactory<AgenteDbContext>
{
    public AgenteDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AgenteDbContext>().UseNpgsql("Host=localhost;Database=agente").Options);
}
