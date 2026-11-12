using Api.Rag.Application.Base;
using Api.Rag.Infrastructure.Busca;
using Api.Rag.Infrastructure.Persistencia;
using Pgvector;
using DominioClassificacao = Api.Rag.Domain.Documentos.ClassificacaoDocumento;

namespace Api.Rag.Tests.Persistencia;

/// <summary>
/// Documentos oficiais de outros tipos (por exemplo, manual estadual) entram na busca quando pedidos, pela
/// vigência no período; a versão continua valendo só para o tipo principal (research R-39).
/// </summary>
[Trait("Category", "Docker")]
public class BuscaTiposAdicionaisTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Modelo = "modelo-teste-tipos-adicionais";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static float[] Eixo(int indice)
    {
        var valores = new float[1536];
        valores[indice] = 1f;
        return valores;
    }

    private static DocumentoEntidade Documento(string tipo, string versao, DateOnly? inicio, DateOnly? fim) => new()
    {
        Id = Guid.NewGuid(),
        Classificacao = ClassificacaoDocumento.Oficial,
        TipoDocumento = tipo,
        Titulo = tipo,
        Origem = "teste",
        Versao = versao,
        Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        Status = StatusDocumento.Ativo,
        ModeloEmbedding = Modelo,
        Dimensao = 1536,
        VigenciaInicio = inicio,
        VigenciaFim = fim,
        IngeridoEm = DateTimeOffset.UtcNow,
        IngeridoPor = "admin",
    };

    private static TrechoEntidade Trecho(Guid documentoId, string texto) => new()
    {
        Id = Guid.NewGuid(),
        DocumentoId = documentoId,
        Ordem = 1,
        Classificacao = ClassificacaoDocumento.Oficial,
        Localizacao = """{"paginaInicio":1,"paginaFim":1}""",
        Texto = texto,
        TextoHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        Embedding = new Vector(Eixo(0)),
    };

    [Fact]
    public async Task Tipos_adicionais_entram_pela_vigencia_e_o_principal_continua_filtrado()
    {
        var sufixo = Guid.NewGuid().ToString("N")[..6];
        var principal = Documento($"guia_{sufixo}", "3.2.4", null, null);
        var estadualVigente = Documento($"outro_{sufixo}", "2025.02", new DateOnly(2025, 7, 1), null);
        var estadualVencido = Documento($"outro_{sufixo}", "2020.01", new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31));
        var naoPedido = Documento($"legislacao_{sufixo}", "1", null, null);
        await using (var banco = fixture.Criar())
        {
            banco.Documentos.AddRange(principal, estadualVigente, estadualVencido, naoPedido);
            banco.Trechos.AddRange(
                Trecho(principal.Id, "trecho do guia"),
                Trecho(estadualVigente.Id, "trecho do manual estadual vigente"),
                Trecho(estadualVencido.Id, "trecho do manual estadual vencido"),
                Trecho(naoPedido.Id, "trecho de legislação não pedida"));
            await banco.SaveChangesAsync(Cancelamento);
        }

        await using var consulta = fixture.Criar();
        var busca = new BuscaHibrida(consulta);
        ConsultaTrechos Consulta(IReadOnlyList<string> adicionais) => new(
            "trecho", Eixo(0), Modelo, principal.TipoDocumento, null, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31),
            new HashSet<DominioClassificacao> { DominioClassificacao.Oficial }, 10, adicionais);

        var comAdicionais = await busca.BuscarAsync(Consulta([estadualVigente.TipoDocumento]), Cancelamento);
        var semAdicionais = await busca.BuscarAsync(Consulta([]), Cancelamento);

        Assert.Equal(
            new[] { "trecho do guia", "trecho do manual estadual vigente" }.Order(),
            comAdicionais.Select(t => t.Texto).Order());
        Assert.Equal(["trecho do guia"], semAdicionais.Select(t => t.Texto));
    }
}
