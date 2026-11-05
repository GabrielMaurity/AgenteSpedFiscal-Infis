using Api.Rag.Application.Consumo;
using Api.Rag.Infrastructure.Persistencia;

namespace Api.Rag.Infrastructure.Consumo;

/// <summary>
/// Grava o consumo por chamada e tentativa. Consumo desconhecido fica nulo, nunca zero (P-17). O custo
/// fica nulo até haver preço de referência nesta API, pois a tabela de preços é da Api.Agente (P-07).
/// </summary>
public sealed class RegistradorConsumoEmbeddings(RagDbContext banco) : IRegistradorConsumoEmbeddings
{
    public async Task RegistrarAsync(RegistroConsumoEmbeddings registro, CancellationToken cancelamento)
    {
        banco.ConsumosEmbeddings.Add(Montar(registro));
        await banco.SaveChangesAsync(cancelamento);
    }

    public static ConsumoEmbeddingEntidade Montar(RegistroConsumoEmbeddings registro) => new()
    {
        Id = Guid.CreateVersion7(),
        DocumentoId = registro.DocumentoId,
        CorrelacaoId = registro.CorrelacaoId,
        Provedor = registro.Provedor,
        Modelo = registro.Modelo,
        Operacao = "embedding",
        Tentativa = registro.Tentativa,
        TokensEntrada = registro.TokensEntrada,
        OrigemTokens = registro.TokensEntrada is null ? "desconhecido" : "informado",
        Inicio = registro.Inicio.ToUniversalTime(),
        DuracaoMs = registro.DuracaoMs,
        Status = registro.Status,
    };
}
