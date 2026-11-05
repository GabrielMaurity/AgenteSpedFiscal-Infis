using Api.Rag.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Testcontainers.PostgreSql;

namespace Api.Rag.Tests.Persistencia;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var banco = Criar();
        await banco.Database.MigrateAsync();
    }

    public RagDbContext Criar() =>
        new(new DbContextOptionsBuilder<RagDbContext>().UseNpgsql(ConnectionString, npgsql => npgsql.UseVector()).Options);

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}

[Trait("Category", "Docker")]
public class BancoRealTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static DocumentoEntidade Documento(string classificacao = ClassificacaoDocumento.Oficial) => new()
    {
        Id = Guid.NewGuid(),
        Classificacao = classificacao,
        TipoDocumento = "guia_pratico_efd_icms_ipi",
        Titulo = "Guia",
        Origem = "SPED",
        Versao = "3.2.4",
        Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        Status = StatusDocumento.Ativo,
        IngeridoEm = DateTimeOffset.UtcNow,
        IngeridoPor = "admin",
    };

    private static TrechoEntidade Trecho(Guid documentoId, string texto, float eixo = 1f)
    {
        var valores = new float[1536];
        valores[0] = eixo;
        return new TrechoEntidade
        {
            Id = Guid.NewGuid(),
            DocumentoId = documentoId,
            Ordem = 1,
            Classificacao = ClassificacaoDocumento.Oficial,
            Localizacao = """{"registro":"C190","paginaInicio":1,"paginaFim":1}""",
            Texto = texto,
            TextoHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Embedding = new Vector(valores),
        };
    }

    [Fact]
    public async Task Migracao_cria_indices_hnsw_e_gin()
    {
        await using var conexao = new NpgsqlConnection(fixture.ConnectionString);
        await conexao.OpenAsync(Cancelamento);
        await using var comando = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'trecho'", conexao);
        var indices = new List<string>();
        await using (var leitor = await comando.ExecuteReaderAsync(Cancelamento))
        {
            while (await leitor.ReadAsync(Cancelamento))
            {
                indices.Add(leitor.GetString(0));
            }
        }

        Assert.Contains(indices, i => i.Contains("hnsw", StringComparison.Ordinal) && i.Contains("vector_cosine_ops", StringComparison.Ordinal));
        Assert.Contains(indices, i => i.Contains("gin", StringComparison.Ordinal) && i.Contains("tsv", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trecho_gera_tsv_e_e_recuperado_por_similaridade()
    {
        await using var banco = fixture.Criar();
        var documento = Documento();
        banco.Documentos.Add(documento);
        banco.Trechos.Add(Trecho(documento.Id, "Registro analítico do documento"));
        await banco.SaveChangesAsync(Cancelamento);

        var trecho = await banco.Trechos.AsNoTracking().SingleAsync(t => t.DocumentoId == documento.Id, Cancelamento);

        Assert.NotNull(trecho.Tsv);
        Assert.Equal(1536, trecho.Embedding.ToArray().Length);
    }

    [Fact]
    public async Task Classificacao_invalida_e_recusada_pelo_banco()
    {
        await using var banco = fixture.Criar();
        banco.Documentos.Add(Documento("publico"));

        await Assert.ThrowsAsync<DbUpdateException>(() => banco.SaveChangesAsync(Cancelamento));
    }

    [Fact]
    public async Task Classificacao_e_imutavel_apos_a_ingestao()
    {
        await using var banco = fixture.Criar();
        var documento = Documento();
        banco.Documentos.Add(documento);
        await banco.SaveChangesAsync(Cancelamento);

        documento.Classificacao = ClassificacaoDocumento.Interno;

        await Assert.ThrowsAsync<InvalidOperationException>(() => banco.SaveChangesAsync(Cancelamento));
    }

    [Fact]
    public async Task Mesmo_arquivo_nao_e_ingerido_duas_vezes()
    {
        await using var banco = fixture.Criar();
        var primeiro = Documento();
        var repetido = Documento();
        repetido.Sha256 = primeiro.Sha256;
        banco.Documentos.AddRange(primeiro, repetido);

        await Assert.ThrowsAsync<DbUpdateException>(() => banco.SaveChangesAsync(Cancelamento));
    }

    [Fact]
    public async Task Mesmo_texto_nao_se_repete_no_documento()
    {
        await using var banco = fixture.Criar();
        var documento = Documento();
        banco.Documentos.Add(documento);
        var a = Trecho(documento.Id, "a");
        var b = Trecho(documento.Id, "b");
        b.TextoHash = a.TextoHash;
        banco.Trechos.AddRange(a, b);

        await Assert.ThrowsAsync<DbUpdateException>(() => banco.SaveChangesAsync(Cancelamento));
    }
}
