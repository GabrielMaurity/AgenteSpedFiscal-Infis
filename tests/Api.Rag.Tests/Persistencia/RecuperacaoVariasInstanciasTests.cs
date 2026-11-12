using Api.Rag.Infrastructure.Persistencia;

namespace Api.Rag.Tests.Persistencia;

/// <summary>Com várias instâncias, só ingestões iniciadas antes do limiar são retomadas (feature 002, FR-P06).</summary>
[Trait("Category", "Docker")]
public class RecuperacaoVariasInstanciasTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static DocumentoEntidade Documento(string status, DateTimeOffset ingeridoEm) => new()
    {
        Id = Guid.NewGuid(),
        Classificacao = ClassificacaoDocumento.Oficial,
        TipoDocumento = "guia_pratico_efd_icms_ipi",
        Titulo = "Guia",
        Origem = "SPED",
        Versao = Guid.NewGuid().ToString("N")[..8],
        Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        Status = status,
        IngeridoEm = ingeridoEm,
        IngeridoPor = "admin",
    };

    [Fact]
    public async Task Pendentes_com_limiar_excluem_ingestoes_recentes()
    {
        var agora = DateTimeOffset.UtcNow;
        var recente = Documento(StatusDocumento.Processando, agora.AddMinutes(-1));
        var antigo = Documento(StatusDocumento.Processando, agora.AddMinutes(-30));
        var recebidoAntigo = Documento(StatusDocumento.Recebido, agora.AddMinutes(-30));
        var ativo = Documento(StatusDocumento.Ativo, agora.AddMinutes(-30));
        await using (var banco = fixture.Criar())
        {
            banco.Documentos.AddRange(recente, antigo, recebidoAntigo, ativo);
            await banco.SaveChangesAsync(Cancelamento);
        }

        await using var consulta = fixture.Criar();
        var repositorio = new RepositorioDocumentos(consulta);
        var comLimiar = await repositorio.ListarPendentesAsync(agora.AddMinutes(-10), Cancelamento);
        var semLimiar = await repositorio.ListarPendentesAsync(null, Cancelamento);

        // O banco é compartilhado com outros testes da classe: só os documentos deste teste importam.
        Guid[] deste = [recente.Id, antigo.Id, recebidoAntigo.Id, ativo.Id];
        Assert.Equal(new[] { antigo.Id, recebidoAntigo.Id }.Order(), comLimiar.Where(deste.Contains).Order());
        Assert.Equal(new[] { recente.Id, antigo.Id, recebidoAntigo.Id }.Order(), semLimiar.Where(deste.Contains).Order());
    }
}
