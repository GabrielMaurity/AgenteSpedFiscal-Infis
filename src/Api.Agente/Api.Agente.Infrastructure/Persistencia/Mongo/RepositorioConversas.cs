using Api.Agente.Application.Conversas;
using Api.Agente.Domain.Conversas;
using MongoDB.Driver;

namespace Api.Agente.Infrastructure.Persistencia.Mongo;

/// <summary>Coleção <c>conversas</c>: toda leitura filtra por organização e usuário (FR-054).</summary>
public sealed class RepositorioConversas(IMongoDatabase banco) : IRepositorioConversas
{
    public const string Colecao = "conversas";

    private readonly IMongoCollection<Conversa> _colecao = banco.GetCollection<Conversa>(Colecao);

    public static Task CriarIndicesAsync(IMongoDatabase banco, CancellationToken cancelamento)
    {
        var chaves = Builders<Conversa>.IndexKeys;
        return banco.GetCollection<Conversa>(Colecao).Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Conversa>(chaves.Ascending(c => c.OrgId).Ascending(c => c.UsuarioId)),
                new CreateIndexModel<Conversa>(chaves.Ascending(c => c.ExpiraEm), new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }),
            ],
            cancelamento);
    }

    public Task SalvarAsync(Conversa conversa, CancellationToken cancelamento) =>
        _colecao.ReplaceOneAsync(
            c => c.Id == conversa.Id && c.OrgId == conversa.OrgId && c.UsuarioId == conversa.UsuarioId,
            conversa,
            new ReplaceOptions { IsUpsert = true },
            cancelamento);

    public async Task<Conversa?> ObterAsync(string orgId, string usuarioId, Guid id, CancellationToken cancelamento) =>
        await _colecao.Find(c => c.Id == id && c.OrgId == orgId && c.UsuarioId == usuarioId).FirstOrDefaultAsync(cancelamento);
}
