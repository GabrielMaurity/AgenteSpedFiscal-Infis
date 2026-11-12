using System.Text.Json;
using AgenteSpedFiscal.Contracts.Mcp;

namespace AgenteSpedFiscal.Evals;

public sealed record ResultadoRecuperacao(string Id, int? PosicaoRelevante, int TrechosDevolvidos);

public sealed record ResultadoConversa(string Conjunto, string Id, bool Aprovado, bool Fundamentada, IReadOnlyList<string> Falhas);

/// <summary>Métricas determinísticas: recall@k, MRR e rubricas das respostas de conversa.</summary>
public static class Metricas
{
    /// <summary>Relevante quando o registro coincide, ou quando capítulo e seção coincidem.</summary>
    public static bool Relevante(LocalizacaoTrecho localizacao, Esperado esperado)
    {
        if (esperado.Registros is { Length: > 0 } registros && localizacao.Registro is { } registro
            && registros.Contains(registro, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return esperado.Secoes is { Length: > 0 } secoes
            && localizacao.Secao is { } secao
            && secoes.Contains(secao, StringComparer.OrdinalIgnoreCase)
            && (esperado.Capitulo is null || string.Equals(esperado.Capitulo, localizacao.Capitulo, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Posição (1..k) do primeiro trecho relevante, ou nulo.</summary>
    public static int? PrimeiraPosicaoRelevante(IReadOnlyList<TrechoRecuperado> trechos, Esperado esperado)
    {
        for (var i = 0; i < trechos.Count; i++)
        {
            if (Relevante(trechos[i].Localizacao, esperado))
            {
                return i + 1;
            }
        }

        return null;
    }

    public static double RecallEmK(IReadOnlyCollection<ResultadoRecuperacao> resultados) =>
        resultados.Count == 0 ? 0 : resultados.Count(r => r.PosicaoRelevante is not null) / (double)resultados.Count;

    public static double Mrr(IReadOnlyCollection<ResultadoRecuperacao> resultados) =>
        resultados.Count == 0 ? 0 : resultados.Sum(r => r.PosicaoRelevante is { } p ? 1.0 / p : 0) / resultados.Count;

    /// <summary>Aplica a rubrica à resposta de <c>POST /api/v1/conversas/{id}/mensagens</c>.</summary>
    public static List<string> Avaliar(JsonElement resposta, Criterios criterios)
    {
        var falhas = new List<string>();
        var fundamentada = resposta.GetProperty("fundamentada").GetBoolean();
        var oficial = resposta.GetProperty("fundamentadaEmFonteOficial").GetBoolean();
        var texto = resposta.GetProperty("resposta").GetString() ?? "";
        var bruto = resposta.GetRawText();
        var referencias = resposta.GetProperty("referencias").EnumerateArray().ToList();
        var limitacoes = resposta.GetProperty("limitacoes").EnumerateArray().Select(l => l.GetString() ?? "").ToList();

        if (criterios.Fundamentada is { } esperadaFundamentada && esperadaFundamentada != fundamentada)
        {
            falhas.Add($"fundamentada = {fundamentada}, esperado {esperadaFundamentada}");
        }

        if (criterios.FundamentadaEmFonteOficial is { } esperadaOficial && esperadaOficial != oficial)
        {
            falhas.Add($"fundamentadaEmFonteOficial = {oficial}, esperado {esperadaOficial}");
        }

        if (criterios.Classificacoes is { } classificacoes)
        {
            var citadas = referencias.Select(r => r.GetProperty("classificacao").GetString()).Distinct().Order().ToArray();
            if (!citadas.SequenceEqual(classificacoes.Order()))
            {
                falhas.Add($"classificações citadas [{string.Join(", ", citadas)}], esperado [{string.Join(", ", classificacoes)}]");
            }
        }

        foreach (var registro in criterios.RegistrosCitados ?? [])
        {
            if (!referencias.Any(r => (r.GetProperty("localizacao").GetString() ?? "").Contains($"Registro {registro}", StringComparison.OrdinalIgnoreCase)))
            {
                falhas.Add($"nenhuma referência ao registro {registro}");
            }
        }

        foreach (var termo in criterios.Contem ?? [])
        {
            if (!texto.Contains(termo, StringComparison.OrdinalIgnoreCase))
            {
                falhas.Add($"resposta sem o termo \"{termo}\"");
            }
        }

        foreach (var termo in criterios.NaoContem ?? [])
        {
            if (bruto.Contains(termo, StringComparison.OrdinalIgnoreCase))
            {
                falhas.Add($"resposta contém o termo proibido \"{termo}\"");
            }
        }

        if (criterios.ContemLimitacao is { } limitacao && !limitacoes.Contains(limitacao))
        {
            falhas.Add($"sem a limitação \"{limitacao}\"");
        }

        if (criterios.ComLimitacao == true && limitacoes.Count == 0)
        {
            falhas.Add("nenhuma limitação declarada");
        }

        return falhas;
    }
}
