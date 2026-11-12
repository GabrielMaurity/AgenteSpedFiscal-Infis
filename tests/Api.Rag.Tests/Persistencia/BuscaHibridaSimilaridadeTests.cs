using Api.Rag.Application.Base;
using Api.Rag.Infrastructure.Busca;
using Api.Rag.Infrastructure.Persistencia;
using Pgvector;
using DominioClassificacao = Api.Rag.Domain.Documentos.ClassificacaoDocumento;

namespace Api.Rag.Tests.Persistencia;

/// <summary>A busca devolve a similaridade de cosseno de cada trecho, de 0 a 1 (research R-38).</summary>
[Trait("Category", "Docker")]
public class BuscaHibridaSimilaridadeTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Modelo = "modelo-teste-similaridade";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static float[] Eixo(int indice)
    {
        var valores = new float[1536];
        valores[indice] = 1f;
        return valores;
    }

    [Fact]
    public async Task Trecho_identico_a_consulta_tem_similaridade_1_e_ortogonal_0()
    {
        var versao = Guid.NewGuid().ToString("N")[..8];
        var documento = new DocumentoEntidade
        {
            Id = Guid.NewGuid(),
            Classificacao = ClassificacaoDocumento.Oficial,
            TipoDocumento = "guia_pratico_efd_icms_ipi",
            Titulo = "Guia",
            Origem = "SPED",
            Versao = versao,
            Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Status = StatusDocumento.Ativo,
            ModeloEmbedding = Modelo,
            Dimensao = 1536,
            IngeridoEm = DateTimeOffset.UtcNow,
            IngeridoPor = "admin",
        };
        TrechoEntidade Trecho(string texto, int eixo, int ordem) => new()
        {
            Id = Guid.NewGuid(),
            DocumentoId = documento.Id,
            Ordem = ordem,
            Classificacao = ClassificacaoDocumento.Oficial,
            Localizacao = """{"registro":"C190","paginaInicio":1,"paginaFim":1}""",
            Texto = texto,
            TextoHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Embedding = new Vector(Eixo(eixo)),
        };
        var proximo = Trecho("totalização do registro analítico", 0, 1);
        var distante = Trecho("inventário físico do estoque", 1, 2);
        await using (var banco = fixture.Criar())
        {
            banco.Documentos.Add(documento);
            banco.Trechos.AddRange(proximo, distante);
            await banco.SaveChangesAsync(Cancelamento);
        }

        await using var consulta = fixture.Criar();
        var encontrados = await new BuscaHibrida(consulta).BuscarAsync(
            new ConsultaTrechos("totalização", Eixo(0), Modelo, "guia_pratico_efd_icms_ipi", versao, null, null,
                new HashSet<DominioClassificacao> { DominioClassificacao.Oficial }, 5),
            Cancelamento);

        Assert.Equal(1.0, encontrados.Single(t => t.TrechoId == proximo.Id).Similaridade!.Value, precision: 4);
        Assert.Equal(0.0, encontrados.Single(t => t.TrechoId == distante.Id).Similaridade!.Value, precision: 4);
        Assert.All(encontrados, t => Assert.InRange(t.Similaridade!.Value, 0.0, 1.0));
    }
}
