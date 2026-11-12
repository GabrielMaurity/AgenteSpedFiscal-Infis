using Api.Agente.Domain.Execucoes;
using Api.Agente.Infrastructure.Persistencia.Mongo;
using Api.Agente.Integration.Tests.Infra;
using MongoDB.Driver;

namespace Api.Agente.Integration.Tests.Persistencia;

/// <summary>Com várias instâncias, a recuperação só alcança execuções paradas além do limiar (feature 002, FR-P06).</summary>
[Collection(ColecaoContainers.Nome)]
[Trait("Category", "Docker")]
public class RecuperacaoVariasInstanciasTests(ContainersFixture containers)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    [Fact]
    public async Task Filtro_por_atualizacao_exclui_execucoes_ativas_em_outra_instancia()
    {
        MongoExtensions.GarantirMapeamento();
        var banco = new MongoClient(containers.MongoConnectionString).GetDatabase($"testes_{Guid.NewGuid():N}");
        var agora = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        // Cada repositório grava com o próprio relógio: AtualizadoEm reflete a última gravação.
        var ativa = Execucao.Criar(Guid.NewGuid(), "org", "u", "c1", CanalExecucao.Rest, agora);
        var parada = Execucao.Criar(Guid.NewGuid(), "org", "u", "c2", CanalExecucao.Rest, agora.AddMinutes(-30));
        var concluida = Execucao.Criar(Guid.NewGuid(), "org", "u", "c3", CanalExecucao.Rest, agora.AddMinutes(-30));
        concluida.Concluir(analiseIaAceita: false);
        await new RepositorioExecucoes(banco, new RelogioFixo(agora.AddMinutes(-1))).SalvarAsync(ativa, Cancelamento);
        await new RepositorioExecucoes(banco, new RelogioFixo(agora.AddMinutes(-30))).SalvarAsync(parada, Cancelamento);
        await new RepositorioExecucoes(banco, new RelogioFixo(agora.AddMinutes(-30))).SalvarAsync(concluida, Cancelamento);
        var repositorio = new RepositorioExecucoes(banco, new RelogioFixo(agora));

        var comLimiar = await repositorio.ListarEmAndamentoAsync(agora.AddMinutes(-10), Cancelamento);
        var semLimiar = await repositorio.ListarEmAndamentoAsync(null, Cancelamento);

        Assert.Equal([parada.Id], comLimiar.Select(e => e.Id));
        Assert.Equal(new[] { ativa.Id, parada.Id }.Order(), semLimiar.Select(e => e.Id).Order());
    }
}
