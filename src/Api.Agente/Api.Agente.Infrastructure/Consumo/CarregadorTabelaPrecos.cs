using System.Reflection;
using System.Text.Json;
using Api.Agente.Infrastructure.Persistencia.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Api.Agente.Infrastructure.Consumo;

public sealed record ArquivoPrecos(string Versao, IReadOnlyList<PrecoModelo> Modelos);

public sealed record PrecoModelo(
    string Provedor,
    string Modelo,
    decimal PrecoEntradaPorMilhao,
    decimal PrecoSaidaPorMilhao,
    string Moeda,
    DateOnly VigenteDesde,
    string Url,
    DateOnly ConsultadoEm);

/// <summary>Carrega data/precos-modelos.json em tabela_precos sem duplicar entradas (research R-21).</summary>
public static class CarregadorTabelaPrecos
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static ArquivoPrecos LerArquivo()
    {
        using var conteudo = Assembly.GetExecutingAssembly().GetManifestResourceStream("precos-modelos.json")
            ?? throw new InvalidOperationException("Recurso precos-modelos.json não encontrado.");
        return JsonSerializer.Deserialize<ArquivoPrecos>(conteudo, Json)
            ?? throw new InvalidOperationException("precos-modelos.json inválido.");
    }

    public static async Task CarregarAsync(AgenteDbContext banco, ArquivoPrecos arquivo, CancellationToken cancelamento)
    {
        foreach (var preco in arquivo.Modelos)
        {
            var existe = await banco.TabelasPrecos.AnyAsync(
                p => p.Provedor == preco.Provedor && p.Modelo == preco.Modelo && p.VigenteDesde == preco.VigenteDesde,
                cancelamento);
            if (existe)
            {
                continue;
            }

            banco.TabelasPrecos.Add(new TabelaPreco
            {
                Id = Guid.CreateVersion7(),
                Provedor = preco.Provedor,
                Modelo = preco.Modelo,
                PrecoEntradaPorMilhao = preco.PrecoEntradaPorMilhao,
                PrecoSaidaPorMilhao = preco.PrecoSaidaPorMilhao,
                Moeda = preco.Moeda,
                VigenteDesde = preco.VigenteDesde,
                Referencia = $"{preco.Url} (consultado em {preco.ConsultadoEm:yyyy-MM-dd})",
                VersaoArquivo = arquivo.Versao,
            });
        }

        await banco.SaveChangesAsync(cancelamento);
    }
}
