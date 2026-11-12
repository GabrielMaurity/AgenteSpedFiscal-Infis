using System.Text.Json.Serialization;

namespace AgenteSpedFiscal.Contracts.Mcp;

/// <summary>Contrato das ferramentas MCP da Api.RAG; ver specs/001-*/contracts/mcp-api-rag.md.</summary>
public static class ContratoMcp
{
    public const string Versao = "1.0.0";
    public const string BuscarTrechos = "buscar_trechos";
    public const string ObterTrecho = "obter_trecho";
    public const string ListarDocumentos = "listar_documentos";
    public const string ErroNaoEncontrado = "nao_encontrado";
}

[JsonConverter(typeof(JsonStringEnumConverter<Classificacao>))]
public enum Classificacao
{
    [JsonStringEnumMemberName("oficial")]
    Oficial,

    [JsonStringEnumMemberName("interno")]
    Interno,
}

public sealed record BuscarTrechosRequest(
    string Consulta,
    string TipoDocumento,
    string CorrelacaoId,
    string? Versao = null,
    string? Periodo = null,
    IReadOnlyList<Classificacao>? Classificacoes = null,
    int? TopK = null)
{
    public const int TamanhoMaximoConsulta = 1_000;
    public const int TopKMaximo = 20;

    /// <summary>Retorna a mensagem de erro do contrato, ou nulo quando a requisição é válida.</summary>
    public string? Validar()
    {
        if (string.IsNullOrWhiteSpace(Consulta) || Consulta.Length > TamanhoMaximoConsulta)
        {
            return $"consulta deve ter de 1 a {TamanhoMaximoConsulta} caracteres.";
        }

        if (string.IsNullOrWhiteSpace(TipoDocumento))
        {
            return "tipoDocumento é obrigatório.";
        }

        if (string.IsNullOrWhiteSpace(CorrelacaoId))
        {
            return "correlacaoId é obrigatório.";
        }

        if (string.IsNullOrWhiteSpace(Versao) == string.IsNullOrWhiteSpace(Periodo))
        {
            return "informe exatamente um entre versao e periodo.";
        }

        if (Periodo is not null && !PeriodoValido(Periodo))
        {
            return "periodo deve estar no formato AAAA-MM.";
        }

        if (TopK is < 1 or > TopKMaximo)
        {
            return $"topK deve estar entre 1 e {TopKMaximo}.";
        }

        return null;
    }

    private static bool PeriodoValido(string periodo) =>
        periodo.Length == 7
        && periodo[4] == '-'
        && periodo[..4].All(char.IsAsciiDigit)
        && int.TryParse(periodo[5..], out var mes)
        && mes is >= 1 and <= 12;
}

public sealed record DocumentoResumo(Guid Id, string Titulo, string Versao, string TipoDocumento);

public sealed record LocalizacaoTrecho(string? Capitulo, string? Secao, string? Registro, int? PaginaInicio, int? PaginaFim);

/// <param name="Pontuacao">Nota da fusão RRF, só para ordenar.</param>
/// <param name="Similaridade">Cosseno entre consulta e trecho, de 0 a 1; nulo em obter_trecho (research R-38).</param>
public sealed record TrechoRecuperado(
    Guid TrechoId,
    string Texto,
    Classificacao Classificacao,
    DocumentoResumo Documento,
    LocalizacaoTrecho Localizacao,
    double Pontuacao,
    double? Similaridade = null);

public sealed record BuscarTrechosResponse(string VersaoContrato, IReadOnlyList<TrechoRecuperado> Trechos, bool SemResultados);

public sealed record ObterTrechoRequest(Guid TrechoId, string CorrelacaoId);

public sealed record ObterTrechoResponse(string VersaoContrato, TrechoRecuperado Trecho);

public sealed record ListarDocumentosRequest(
    string CorrelacaoId,
    string? TipoDocumento = null,
    Classificacao? Classificacao = null,
    string? Periodo = null);

public sealed record DocumentoAtivo(
    Guid Id,
    Classificacao Classificacao,
    string Titulo,
    string Versao,
    DateOnly? VigenciaInicio,
    DateOnly? VigenciaFim);

public sealed record ListarDocumentosResponse(string VersaoContrato, IReadOnlyList<DocumentoAtivo> Documentos);
