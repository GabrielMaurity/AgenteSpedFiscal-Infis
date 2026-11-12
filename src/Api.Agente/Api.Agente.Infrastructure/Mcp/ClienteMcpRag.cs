using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using AgenteSpedFiscal.Contracts.Mcp;
using Api.Agente.Application.Analise;
using Api.Agente.Domain.Analise;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Infrastructure.Resiliencia;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Api.Agente.Infrastructure.Mcp;

/// <summary>
/// Cliente MCP da Api.Rag (contrato mcp-api-rag.md, ADR-0005). Usa o HttpClient com a resiliência de
/// research R-22, envia o token de serviço e o X-Correlation-Id, e guarda no contexto todo trecho
/// devolvido, com a classificação, para a validação das citações (P-13, R-27).
/// </summary>
public sealed class ClienteMcpRag(
    IHttpClientFactory clientes,
    IOptions<McpRagOpcoes> opcoes,
    IOptions<BaseInformacaoOpcoes> opcoesBase,
    ILoggerFactory logs) : IBaseInformacao
{
    public const string CabecalhoCorrelacao = "X-Correlation-Id";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILogger _logger = logs.CreateLogger<ClienteMcpRag>();

    public async Task<ResultadoBusca> BuscarAsync(ConsultaBase consulta, ContextoRecuperacao contexto, CancellationToken cancelamento)
    {
        var argumentos = new Dictionary<string, object?>
        {
            ["consulta"] = consulta.Consulta,
            ["tipoDocumento"] = opcoesBase.Value.TipoDocumento,
            ["correlacaoId"] = consulta.CorrelacaoId,
            ["versao"] = consulta.Versao,
            ["periodo"] = consulta.Periodo,
            ["topK"] = consulta.TopK ?? opcoesBase.Value.TopK,
        };
        if (opcoesBase.Value.TiposOficiaisAdicionais is { Length: > 0 } adicionais)
        {
            argumentos["tiposDocumentoAdicionais"] = adicionais;
        }

        var resultado = await ChamarAsync(ContratoMcp.BuscarTrechos, argumentos, consulta.CorrelacaoId, cancelamento);
        if (resultado.IsError == true)
        {
            throw new ConsultaInvalidaException(Texto(resultado));
        }

        var resposta = Ler<BuscarTrechosResponse>(resultado);
        var trechos = resposta.Trechos.Select(Converter).ToList();
        contexto.Registrar(trechos);
        return new ResultadoBusca(trechos, resposta.SemResultados || trechos.Count == 0);
    }

    public async Task<TrechoBase?> ObterTrechoAsync(string trechoId, string correlacaoId, ContextoRecuperacao contexto, CancellationToken cancelamento)
    {
        if (!Guid.TryParse(trechoId, out var id))
        {
            throw new ConsultaInvalidaException("trechoId deve ser um identificador válido.");
        }

        var resultado = await ChamarAsync(
            ContratoMcp.ObterTrecho,
            new Dictionary<string, object?> { ["trechoId"] = id, ["correlacaoId"] = correlacaoId },
            correlacaoId,
            cancelamento);

        if (resultado.IsError == true)
        {
            var mensagem = Texto(resultado);
            return mensagem.Contains(ContratoMcp.ErroNaoEncontrado, StringComparison.Ordinal) ? null : throw new ConsultaInvalidaException(mensagem);
        }

        var trecho = Converter(Ler<ObterTrechoResponse>(resultado).Trecho);
        contexto.Registrar([trecho]);
        return trecho;
    }

    public static string Localizacao(LocalizacaoTrecho l)
    {
        var partes = new List<string>();
        if (l.Capitulo is not null)
        {
            partes.Add($"Cap. {l.Capitulo}");
        }

        if (l.Secao is not null)
        {
            partes.Add($"Seção {l.Secao}");
        }

        if (l.Registro is not null)
        {
            partes.Add($"Registro {l.Registro}");
        }

        if (l.PaginaInicio is { } inicio)
        {
            partes.Add(l.PaginaFim is { } fim && fim != inicio
                ? string.Create(CultureInfo.InvariantCulture, $"p. {inicio}–{fim}")
                : string.Create(CultureInfo.InvariantCulture, $"p. {inicio}"));
        }

        return partes.Count > 0 ? string.Join(", ", partes) : "não informada";
    }

    private static TrechoBase Converter(TrechoRecuperado t) => new(
        t.TrechoId.ToString(),
        t.Texto,
        t.Classificacao == Classificacao.Oficial ? ClassificacaoFonte.Oficial : ClassificacaoFonte.Interno,
        t.Documento.Titulo,
        t.Documento.Versao,
        Localizacao(t.Localizacao),
        t.Similaridade);

    /// <summary>Falhas de rede, autenticação ou protocolo viram indisponibilidade: nunca resposta sem fundamento.</summary>
    private async Task<CallToolResult> ChamarAsync(string ferramenta, Dictionary<string, object?> argumentos, string correlacaoId, CancellationToken cancelamento)
    {
        // Sem token, a chamada vai sem Authorization: só funciona com a Api.Rag em modo sem autenticação
        // (Development); com autenticação ligada, a Api.Rag responde 401 e a base fica indisponível.
        var http = clientes.CreateClient(ClientesHttp.McpRag);
        if (opcoes.Value.TokenServico is { Length: > 0 } token)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        http.DefaultRequestHeaders.Add(CabecalhoCorrelacao, correlacaoId);

        try
        {
            var transporte = new HttpClientTransport(
                new HttpClientTransportOptions { Endpoint = new Uri(opcoes.Value.Url), TransportMode = HttpTransportMode.StreamableHttp },
                http,
                logs,
                ownsHttpClient: true);
            await using var cliente = await McpClient.CreateAsync(transporte, cancellationToken: cancelamento);
            return await cliente.CallToolAsync(ferramenta, argumentos, cancellationToken: cancelamento);
        }
        catch (Exception excecao) when (excecao is HttpRequestException or McpException or TimeoutException or IOException or JsonException
            || (excecao is OperationCanceledException && !cancelamento.IsCancellationRequested))
        {
            _logger.LogWarning(excecao, "Falha ao chamar {Ferramenta} na Api.Rag.", ferramenta);
            throw new BaseIndisponivelException("A base de informação não respondeu.", excecao);
        }
    }

    private static T Ler<T>(CallToolResult resultado)
    {
        try
        {
            return (resultado.StructuredContent is { } estruturado
                    ? estruturado.Deserialize<T>(Json)
                    : JsonSerializer.Deserialize<T>(Texto(resultado), Json))
                ?? throw new JsonException("Resposta vazia.");
        }
        catch (JsonException erro)
        {
            throw new BaseIndisponivelException("Resposta da base fora do contrato MCP.", erro);
        }
    }

    private static string Texto(CallToolResult resultado) =>
        string.Concat(resultado.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
