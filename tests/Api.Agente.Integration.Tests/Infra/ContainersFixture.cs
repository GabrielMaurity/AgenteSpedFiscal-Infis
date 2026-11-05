using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;

namespace Api.Agente.Integration.Tests.Infra;

/// <summary>PostgreSQL e MongoDB reais para os testes marcados com Category=Docker.</summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0").Build();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public string MongoConnectionString => _mongo.GetConnectionString();

    public async ValueTask InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _mongo.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _mongo.DisposeAsync();
    }
}

[CollectionDefinition(Nome)]
public sealed class ColecaoContainers : ICollectionFixture<ContainersFixture>
{
    public const string Nome = "Containers";
}
