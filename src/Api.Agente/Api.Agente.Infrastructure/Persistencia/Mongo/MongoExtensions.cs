using Api.Agente.Application.Execucoes;
using Api.Agente.Domain.Execucoes;
using Api.Agente.Infrastructure.Configuracao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace Api.Agente.Infrastructure.Persistencia.Mongo;

public static class MongoExtensions
{
    public const string ColecaoExecucoes = "execucoes";

    private static readonly Lazy<bool> Mapeamento = new(() =>
    {
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        // BSON Date: o índice TTL só funciona com esse tipo.
        BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.DateTime));

        var convencoes = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true),
        };
        ConventionRegistry.Register(
            "agente",
            convencoes,
            tipo => tipo.Namespace?.StartsWith("Api.Agente.Domain", StringComparison.Ordinal) == true);
        return true;
    });

    public static void GarantirMapeamento() => _ = Mapeamento.Value;

    public static IServiceCollection AddMongoAgente(this IServiceCollection services)
    {
        GarantirMapeamento();

        // MongoClient é thread-safe e mantém o pool de conexões: uma instância por processo.
        services.AddSingleton<IMongoClient>(provedor =>
            new MongoClient(provedor.GetRequiredService<IOptions<MongoDbOpcoes>>().Value.ConnectionString));
        services.AddSingleton(provedor => provedor.GetRequiredService<IMongoClient>()
            .GetDatabase(provedor.GetRequiredService<IOptions<MongoDbOpcoes>>().Value.Database));
        services.AddSingleton<IRepositorioExecucoes, RepositorioExecucoes>();
        return services;
    }

    public static async Task CriarIndicesAsync(IMongoDatabase banco, CancellationToken cancelamento)
    {
        var chaves = Builders<ExecucaoEstado>.IndexKeys;
        await banco.GetCollection<ExecucaoEstado>(ColecaoExecucoes).Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ExecucaoEstado>(chaves.Ascending(e => e.OrgId).Descending(e => e.CriadoEm)),
                new CreateIndexModel<ExecucaoEstado>(chaves.Ascending(e => e.OrgId).Ascending(e => e.UsuarioId)),
                new CreateIndexModel<ExecucaoEstado>(
                    chaves.Ascending(e => e.ExpiraEm),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            ],
            cancelamento);
    }
}

public sealed class RepositorioExecucoes(IMongoDatabase banco, TimeProvider relogio) : IRepositorioExecucoes
{
    private readonly IMongoCollection<ExecucaoEstado> _colecao =
        banco.GetCollection<ExecucaoEstado>(MongoExtensions.ColecaoExecucoes);

    public Task SalvarAsync(Execucao execucao, CancellationToken cancelamento) =>
        _colecao.ReplaceOneAsync(
            e => e.Id == execucao.Id && e.OrgId == execucao.OrgId,
            execucao.Exportar(relogio.GetUtcNow()),
            new ReplaceOptions { IsUpsert = true },
            cancelamento);

    public async Task<Execucao?> ObterAsync(string orgId, Guid id, CancellationToken cancelamento)
    {
        var estado = await _colecao.Find(e => e.Id == id && e.OrgId == orgId).FirstOrDefaultAsync(cancelamento);
        return estado is null ? null : Execucao.Restaurar(estado);
    }

    public async Task<IReadOnlyList<Execucao>> ListarAsync(string orgId, int pagina, int tamanho, CancellationToken cancelamento)
    {
        var estados = await _colecao.Find(e => e.OrgId == orgId)
            .SortByDescending(e => e.CriadoEm)
            .Skip((pagina - 1) * tamanho)
            .Limit(tamanho)
            .ToListAsync(cancelamento);
        return estados.ConvertAll(Execucao.Restaurar);
    }

    public async Task<IReadOnlyList<Execucao>> ListarEmAndamentoAsync(CancellationToken cancelamento)
    {
        var estados = await _colecao.Find(e =>
                e.Estado != EstadoExecucao.Concluida
                && e.Estado != EstadoExecucao.ConcluidaComLimitacoes
                && e.Estado != EstadoExecucao.Falha)
            .ToListAsync(cancelamento);
        return estados.ConvertAll(Execucao.Restaurar);
    }

    public async Task<bool> ExcluirAsync(string orgId, Guid id, CancellationToken cancelamento)
    {
        var resultado = await _colecao.DeleteOneAsync(e => e.Id == id && e.OrgId == orgId, cancelamento);
        return resultado.DeletedCount > 0;
    }
}

public sealed class MongoHealthCheck(IMongoDatabase banco) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await banco.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception excecao) when (excecao is MongoException or TimeoutException)
        {
            // A descrição não é exposta ao chamador; o endpoint só informa o estado.
            return new HealthCheckResult(context.Registration.FailureStatus, "MongoDB indisponível.");
        }
    }
}
