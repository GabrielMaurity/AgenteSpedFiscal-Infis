using Api.Agente.Application.Consumo;
using Api.Agente.Infrastructure.Persistencia.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Api.Agente.Infrastructure.Consumo;

/// <summary>Grava o consumo por chamada e tentativa. Consumo desconhecido fica nulo, nunca zero (P-17).</summary>
public sealed class RegistradorConsumo(AgenteDbContext banco) : IRegistradorConsumo
{
    public async Task RegistrarAsync(RegistroConsumo registro, CancellationToken cancelamento)
    {
        var inicio = registro.Inicio.ToUniversalTime();
        var data = DateOnly.FromDateTime(inicio.UtcDateTime);

        var preco = await banco.TabelasPrecos
            .Where(p => p.Provedor == registro.Provedor && p.Modelo == registro.Modelo && p.VigenteDesde <= data)
            .OrderByDescending(p => p.VigenteDesde)
            .FirstOrDefaultAsync(cancelamento);

        banco.ConsumosIa.Add(Montar(registro, preco));
        await banco.SaveChangesAsync(cancelamento);
    }

    public static ConsumoIa Montar(RegistroConsumo registro, TabelaPreco? preco)
    {
        var tokensConhecidos = registro.TokensEntrada is not null && registro.TokensSaida is not null;
        var custo = tokensConhecidos && preco is not null
            ? ((registro.TokensEntrada!.Value * preco.PrecoEntradaPorMilhao) + (registro.TokensSaida!.Value * preco.PrecoSaidaPorMilhao)) / 1_000_000m
            : (decimal?)null;

        return new ConsumoIa
        {
            Id = Guid.CreateVersion7(),
            ExecucaoId = registro.ExecucaoId,
            CorrelacaoId = registro.CorrelacaoId,
            OrgId = registro.OrgId,
            Provedor = registro.Provedor,
            Modelo = registro.Modelo,
            Operacao = registro.Operacao,
            Tentativa = registro.Tentativa,
            TokensEntrada = registro.TokensEntrada,
            TokensSaida = registro.TokensSaida,
            OrigemTokens = tokensConhecidos ? OrigemTokens.Informado : OrigemTokens.Desconhecido,
            CustoEstimado = custo,
            Moeda = custo is null ? null : preco!.Moeda,
            TabelaPrecosId = custo is null ? null : preco!.Id,
            Inicio = registro.Inicio.ToUniversalTime(),
            DuracaoMs = registro.DuracaoMs,
            Status = registro.Status,
        };
    }
}
