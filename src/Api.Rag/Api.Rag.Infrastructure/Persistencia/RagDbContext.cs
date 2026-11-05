using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;
using NpgsqlTypes;
using Pgvector;

namespace Api.Rag.Infrastructure.Persistencia;

public static class ClassificacaoDocumento
{
    public const string Oficial = "oficial";
    public const string Interno = "interno";
}

public static class StatusDocumento
{
    public const string Recebido = "recebido";
    public const string Processando = "processando";
    public const string Ativo = "ativo";
    public const string Falha = "falha";
    public const string Inativo = "inativo";
}

public sealed class DocumentoEntidade
{
    public Guid Id { get; set; }
    public required string Classificacao { get; set; }
    public required string TipoDocumento { get; set; }
    public required string Titulo { get; set; }
    public required string Origem { get; set; }
    public required string Versao { get; set; }
    public DateOnly? VigenciaInicio { get; set; }
    public DateOnly? VigenciaFim { get; set; }
    public required string Sha256 { get; set; }
    public required string Status { get; set; }
    public string? ModeloEmbedding { get; set; }
    public int? Dimensao { get; set; }
    public DateTimeOffset IngeridoEm { get; set; }
    public required string IngeridoPor { get; set; }
}

public sealed class TrechoEntidade
{
    public Guid Id { get; set; }
    public Guid DocumentoId { get; set; }
    public int Ordem { get; set; }
    public required string Classificacao { get; set; }

    /// <summary>JSON: capitulo, secao, registro, paginaInicio e paginaFim.</summary>
    public required string Localizacao { get; set; }
    public required string Texto { get; set; }
    public required string TextoHash { get; set; }
    public NpgsqlTsVector? Tsv { get; private set; }
    public required Vector Embedding { get; set; }
}

public sealed class ConsumoEmbeddingEntidade
{
    public Guid Id { get; set; }
    public Guid? DocumentoId { get; set; }
    public required string CorrelacaoId { get; set; }
    public required string Provedor { get; set; }
    public required string Modelo { get; set; }
    public required string Operacao { get; set; }
    public int Tentativa { get; set; }
    public int? TokensEntrada { get; set; }
    public required string OrigemTokens { get; set; }
    public decimal? CustoEstimado { get; set; }
    public string? Moeda { get; set; }
    public string? ReferenciaPreco { get; set; }
    public DateTimeOffset Inicio { get; set; }
    public int DuracaoMs { get; set; }
    public required string Status { get; set; }
}

public sealed class RagDbContext(DbContextOptions<RagDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<DocumentoEntidade> Documentos => Set<DocumentoEntidade>();
    public DbSet<TrechoEntidade> Trechos => Set<TrechoEntidade>();
    public DbSet<ConsumoEmbeddingEntidade> ConsumosEmbeddings => Set<ConsumoEmbeddingEntidade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<DocumentoEntidade>(e =>
        {
            e.ToTable("documento", t =>
            {
                t.HasCheckConstraint("ck_documento_classificacao", "classificacao IN ('oficial', 'interno')");
                t.HasCheckConstraint("ck_documento_status", "status IN ('recebido', 'processando', 'ativo', 'falha', 'inativo')");
            });
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Classificacao).HasColumnName("classificacao").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            e.Property(p => p.TipoDocumento).HasColumnName("tipo_documento");
            e.Property(p => p.Titulo).HasColumnName("titulo");
            e.Property(p => p.Origem).HasColumnName("origem");
            e.Property(p => p.Versao).HasColumnName("versao");
            e.Property(p => p.VigenciaInicio).HasColumnName("vigencia_inicio");
            e.Property(p => p.VigenciaFim).HasColumnName("vigencia_fim");
            e.Property(p => p.Sha256).HasColumnName("sha256").HasMaxLength(64).IsFixedLength();
            e.Property(p => p.Status).HasColumnName("status");
            e.Property(p => p.ModeloEmbedding).HasColumnName("modelo_embedding");
            e.Property(p => p.Dimensao).HasColumnName("dimensao");
            e.Property(p => p.IngeridoEm).HasColumnName("ingerido_em");
            e.Property(p => p.IngeridoPor).HasColumnName("ingerido_por");
            e.HasIndex(p => p.Sha256).IsUnique();
            e.HasIndex(p => new { p.TipoDocumento, p.Versao });
        });

        modelBuilder.Entity<TrechoEntidade>(e =>
        {
            e.ToTable("trecho", t => t.HasCheckConstraint("ck_trecho_classificacao", "classificacao IN ('oficial', 'interno')"));
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.DocumentoId).HasColumnName("documento_id");
            e.Property(p => p.Ordem).HasColumnName("ordem");
            e.Property(p => p.Classificacao).HasColumnName("classificacao").Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            e.Property(p => p.Localizacao).HasColumnName("localizacao").HasColumnType("jsonb");
            e.Property(p => p.Texto).HasColumnName("texto");
            e.Property(p => p.TextoHash).HasColumnName("texto_hash").HasMaxLength(64).IsFixedLength();
            e.Property(p => p.Tsv).HasColumnName("tsv").HasComputedColumnSql("to_tsvector('portuguese', texto)", stored: true);
            e.Property(p => p.Embedding).HasColumnName("embedding").HasColumnType("vector(1536)");
            e.HasOne<DocumentoEntidade>().WithMany().HasForeignKey(p => p.DocumentoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.DocumentoId, p.TextoHash }).IsUnique();
            e.HasIndex(p => new { p.DocumentoId, p.Ordem });
            e.HasIndex(p => p.Classificacao);
            e.HasIndex(p => p.Tsv).HasMethod("gin");
            e.HasIndex(p => p.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        modelBuilder.Entity<ConsumoEmbeddingEntidade>(e =>
        {
            e.ToTable("consumo_embeddings", t => t.HasCheckConstraint(
                "ck_consumo_embeddings_origem_tokens", "origem_tokens IN ('informado', 'desconhecido')"));
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.DocumentoId).HasColumnName("documento_id");
            e.Property(p => p.CorrelacaoId).HasColumnName("correlacao_id");
            e.Property(p => p.Provedor).HasColumnName("provedor");
            e.Property(p => p.Modelo).HasColumnName("modelo");
            e.Property(p => p.Operacao).HasColumnName("operacao");
            e.Property(p => p.Tentativa).HasColumnName("tentativa");
            e.Property(p => p.TokensEntrada).HasColumnName("tokens_entrada");
            e.Property(p => p.OrigemTokens).HasColumnName("origem_tokens");
            e.Property(p => p.CustoEstimado).HasColumnName("custo_estimado").HasPrecision(18, 8);
            e.Property(p => p.Moeda).HasColumnName("moeda").HasMaxLength(3).IsFixedLength();
            e.Property(p => p.ReferenciaPreco).HasColumnName("referencia_preco");
            e.Property(p => p.Inicio).HasColumnName("inicio");
            e.Property(p => p.DuracaoMs).HasColumnName("duracao_ms");
            e.Property(p => p.Status).HasColumnName("status");
            e.HasOne<DocumentoEntidade>().WithMany().HasForeignKey(p => p.DocumentoId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(p => p.CorrelacaoId);
        });
    }
}

/// <summary>Usado só por dotnet ef; a geração de migrações não abre conexão.</summary>
public sealed class RagDbContextFactory : IDesignTimeDbContextFactory<RagDbContext>
{
    public RagDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<RagDbContext>()
            .UseNpgsql("Host=localhost;Database=rag", npgsql => npgsql.UseVector())
            .Options);
}
