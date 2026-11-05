using Api.Agente.Application.Consumo;
using Api.Agente.Domain.Execucoes;
using Api.Agente.Infrastructure.Consumo;
using Api.Agente.Infrastructure.Persistencia.Mongo;
using Api.Agente.Infrastructure.Persistencia.Postgres;
using Api.Agente.Integration.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Api.Agente.Integration.Tests.Persistencia;

[Collection(ColecaoContainers.Nome)]
[Trait("Category", "Docker")]
public class BancosRealTests(ContainersFixture containers)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private IMongoDatabase BancoMongo()
    {
        MongoExtensions.GarantirMapeamento();
        return new MongoClient(containers.MongoConnectionString).GetDatabase($"testes_{Guid.NewGuid():N}");
    }

    private async Task<AgenteDbContext> BancoPostgresAsync()
    {
        var banco = new AgenteDbContext(new DbContextOptionsBuilder<AgenteDbContext>()
            .UseNpgsql(containers.PostgresConnectionString).Options);
        await banco.Database.MigrateAsync(Cancelamento);
        return banco;
    }

    private static Execucao NovaExecucao(string orgId) =>
        Execucao.Criar(Guid.NewGuid(), orgId, "usuario", "correlacao", CanalExecucao.Rest, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Indices_incluem_ttl_em_expira_em()
    {
        var banco = BancoMongo();

        await MongoExtensions.CriarIndicesAsync(banco, Cancelamento);

        var indices = await (await banco.GetCollection<BsonDocument>(MongoExtensions.ColecaoExecucoes)
            .Indexes.ListAsync(Cancelamento)).ToListAsync(Cancelamento);
        var ttl = Assert.Single(indices, i => i.Contains("expireAfterSeconds"));
        Assert.Equal(0, ttl["expireAfterSeconds"].ToInt32());
        Assert.True(ttl["key"].AsBsonDocument.Contains("expiraEm"));
        Assert.Contains(indices, i => i["key"].AsBsonDocument.Contains("orgId") && i["key"].AsBsonDocument.Contains("criadoEm"));
        Assert.Contains(indices, i => i["key"].AsBsonDocument.Contains("orgId") && i["key"].AsBsonDocument.Contains("usuarioId"));
    }

    [Fact]
    public async Task Repositorio_salva_le_e_atualiza()
    {
        var repositorio = new RepositorioExecucoes(BancoMongo(), TimeProvider.System);
        var execucao = NovaExecucao("org-a");

        await repositorio.SalvarAsync(execucao, Cancelamento);
        execucao.AvancarPara(EstadoExecucao.ValidandoArquivo);
        await repositorio.SalvarAsync(execucao, Cancelamento);

        var lida = await repositorio.ObterAsync("org-a", execucao.Id, Cancelamento);
        Assert.Equal(EstadoExecucao.ValidandoArquivo, lida?.Estado);
    }

    [Fact]
    public async Task Outra_organizacao_nao_le_nem_exclui()
    {
        var repositorio = new RepositorioExecucoes(BancoMongo(), TimeProvider.System);
        var execucao = NovaExecucao("org-a");
        await repositorio.SalvarAsync(execucao, Cancelamento);

        Assert.Null(await repositorio.ObterAsync("org-b", execucao.Id, Cancelamento));
        Assert.Empty(await repositorio.ListarAsync("org-b", 1, 10, Cancelamento));
        Assert.False(await repositorio.ExcluirAsync("org-b", execucao.Id, Cancelamento));

        Assert.True(await repositorio.ExcluirAsync("org-a", execucao.Id, Cancelamento));
        Assert.Null(await repositorio.ObterAsync("org-a", execucao.Id, Cancelamento));
    }

    [Fact]
    public async Task Consumo_desconhecido_e_gravado_como_nulo()
    {
        await using var banco = await BancoPostgresAsync();
        var execucaoId = Guid.NewGuid();

        await new RegistradorConsumo(banco).RegistrarAsync(
            new RegistroConsumo(execucaoId, "c", "org", "openai", "m", "chat", 1, null, null, DateTimeOffset.UtcNow, 10, "falha"),
            Cancelamento);

        var consumo = await banco.ConsumosIa.AsNoTracking().SingleAsync(c => c.ExecucaoId == execucaoId, Cancelamento);
        Assert.Null(consumo.TokensEntrada);
        Assert.Equal(OrigemTokens.Desconhecido, consumo.OrigemTokens);
    }

    [Fact]
    public async Task Tabela_de_precos_nao_duplica_e_usa_o_preco_vigente()
    {
        await using var banco = await BancoPostgresAsync();
        var modelo = $"m-{Guid.NewGuid():N}";
        var arquivo = new ArquivoPrecos("1.0.0",
        [
            new PrecoModelo("openai", modelo, 1m, 1m, "USD", new DateOnly(2020, 1, 1), "https://exemplo", new DateOnly(2026, 1, 1)),
            new PrecoModelo("openai", modelo, 2m, 2m, "USD", new DateOnly(2025, 1, 1), "https://exemplo", new DateOnly(2026, 1, 1)),
        ]);

        await CarregadorTabelaPrecos.CarregarAsync(banco, arquivo, Cancelamento);
        await CarregadorTabelaPrecos.CarregarAsync(banco, arquivo, Cancelamento);

        Assert.Equal(2, await banco.TabelasPrecos.CountAsync(p => p.Modelo == modelo, Cancelamento));

        var execucaoId = Guid.NewGuid();
        await new RegistradorConsumo(banco).RegistrarAsync(
            new RegistroConsumo(execucaoId, "c", "org", "openai", modelo, "chat", 1, 1_000_000, 0, DateTimeOffset.UtcNow, 10, "sucesso"),
            Cancelamento);
        var consumo = await banco.ConsumosIa.AsNoTracking().SingleAsync(c => c.ExecucaoId == execucaoId, Cancelamento);
        Assert.Equal(2m, consumo.CustoEstimado);
    }
}
