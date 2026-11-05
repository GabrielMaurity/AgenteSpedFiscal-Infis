namespace Api.Rag.Application.Consumo;

/// <summary>Uma chamada de embeddings. Gera um registro por chamada e por tentativa (P-17).</summary>
/// <param name="Tentativa">1 = chamada original; maior que 1 = retentativa.</param>
/// <param name="TokensEntrada">Nulo quando o provedor não informou; nunca zero por omissão.</param>
public sealed record RegistroConsumoEmbeddings(
    Guid? DocumentoId,
    string CorrelacaoId,
    string Provedor,
    string Modelo,
    int Tentativa,
    int? TokensEntrada,
    DateTimeOffset Inicio,
    int DuracaoMs,
    string Status);

public interface IRegistradorConsumoEmbeddings
{
    Task RegistrarAsync(RegistroConsumoEmbeddings registro, CancellationToken cancelamento);
}
