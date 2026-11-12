using Api.Agente.Domain.Achados;

namespace Api.Agente.Domain.Analise;

public enum StatusAnaliseIa
{
    Aceita,
    Falha,
}

public enum ClassificacaoFonte
{
    Oficial,
    Interno,
}

/// <summary>Trecho da base citado em uma resposta. Os dados vêm da Api.Rag, nunca do LLM (P-12).</summary>
public sealed record ReferenciaTrecho(string TrechoId, string Documento, string Versao, string Localizacao, ClassificacaoFonte Classificacao);

public sealed record ExplicacaoAchado(string AchadoId, string Texto, string CausaProvavel, string AcaoCorretiva, List<string> TrechoIds);

public sealed record IndicioAnalise(string Descricao, Severidade Severidade, string? Registro, List<string> TrechoIds);

/// <summary>Conteúdo do LLM já validado, no formato de llm-analise.schema.json (research R-10).</summary>
public sealed record ConteudoAnaliseIa(
    string VersaoContrato,
    string Resumo,
    List<ExplicacaoAchado> Explicacoes,
    List<IndicioAnalise> Indicios,
    List<string> Limitacoes,
    bool RevisaoHumanaRecomendada);

/// <summary>Resultado das etapas 5 a 8. O conteúdo é nulo quando a análise falhou (FR-084).</summary>
public sealed record AnaliseIa(
    StatusAnaliseIa Status,
    string Modelo,
    int Tentativas,
    ConteudoAnaliseIa? Conteudo,
    List<ReferenciaTrecho> Referencias,
    bool FundamentadaEmFonteOficial);

/// <summary>Regras de fundamentação calculadas pelo sistema a partir das classificações citadas (FR-051, R-27).</summary>
public static class Fundamentacao
{
    public const string SemFonteOficial = "Sem fundamento em fonte oficial recuperada.";

    public static bool EmFonteOficial(IEnumerable<ReferenciaTrecho> citadas) =>
        citadas.Any(r => r.Classificacao == ClassificacaoFonte.Oficial);

    /// <summary>Acrescenta a limitação quando há citações, mas nenhuma de fonte oficial.</summary>
    public static List<string> Limitacoes(IEnumerable<string> limitacoes, IReadOnlyCollection<ReferenciaTrecho> citadas)
    {
        var resultado = limitacoes.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct(StringComparer.Ordinal).ToList();
        if (citadas.Count > 0 && !EmFonteOficial(citadas) && !resultado.Contains(SemFonteOficial))
        {
            resultado.Add(SemFonteOficial);
        }

        return resultado;
    }
}
