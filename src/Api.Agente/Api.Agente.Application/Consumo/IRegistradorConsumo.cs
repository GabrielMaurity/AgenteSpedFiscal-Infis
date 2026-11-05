namespace Api.Agente.Application.Consumo;

/// <summary>Uma chamada a provedor de IA. Gera um registro por chamada e por tentativa (P-17).</summary>
/// <param name="Tentativa">1 = chamada original; maior que 1 = retentativa.</param>
/// <param name="TokensEntrada">Nulo quando o provedor não informou; nunca zero por omissão.</param>
/// <param name="TokensSaida">Nulo quando o provedor não informou; nunca zero por omissão.</param>
public sealed record RegistroConsumo(
    Guid ExecucaoId,
    string CorrelacaoId,
    string OrgId,
    string Provedor,
    string Modelo,
    string Operacao,
    int Tentativa,
    int? TokensEntrada,
    int? TokensSaida,
    DateTimeOffset Inicio,
    int DuracaoMs,
    string Status);

public interface IRegistradorConsumo
{
    Task RegistrarAsync(RegistroConsumo registro, CancellationToken cancelamento);
}
