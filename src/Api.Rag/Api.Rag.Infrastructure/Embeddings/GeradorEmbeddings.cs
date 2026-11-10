using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Api.Rag.Application.Base;
using Api.Rag.Application.Consumo;
using Api.Rag.Infrastructure.Configuracao;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Rag.Infrastructure.Embeddings;

/// <summary>
/// Embeddings pela API da OpenAI (research R-14). Cada tentativa, inclusive as retentativas, vira um registro
/// em consumo_embeddings; quando a resposta não informa tokens, o consumo fica desconhecido, nunca zero (P-17).
/// </summary>
public sealed partial class GeradorEmbeddings(
    HttpClient http,
    IOptions<OpenAiOpcoes> opcoes,
    IRegistradorConsumoEmbeddings registrador,
    TimeProvider relogio,
    ILogger<GeradorEmbeddings> logger) : IGeradorEmbeddings
{
    private const string Provedor = "openai";

    public string Modelo => opcoes.Value.ModeloEmbedding;

    public int Dimensao => opcoes.Value.Dimensao;

    private sealed record Requisicao(
        [property: JsonPropertyName("model")] string Modelo,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Entradas);

    private sealed record Item(
        [property: JsonPropertyName("index")] int Indice,
        [property: JsonPropertyName("embedding")] float[] Vetor);

    private sealed record Uso([property: JsonPropertyName("prompt_tokens")] int? TokensEntrada);

    private sealed record Resposta(
        [property: JsonPropertyName("data")] IReadOnlyList<Item> Dados,
        [property: JsonPropertyName("usage")] Uso? Uso);

    public async Task<IReadOnlyList<float[]>> GerarAsync(IReadOnlyList<string> textos, Guid? documentoId, string correlacaoId, CancellationToken cancelamento)
    {
        if (textos.Count == 0)
        {
            return [];
        }

        for (var tentativa = 1; ; tentativa++)
        {
            var inicio = relogio.GetUtcNow();
            var cronometro = Stopwatch.StartNew();
            try
            {
                using var resposta = await http.PostAsJsonAsync("embeddings", new Requisicao(Modelo, textos), cancelamento);
                if (Transitorio(resposta.StatusCode) && tentativa <= opcoes.Value.Retentativas)
                {
                    await RegistrarAsync(documentoId, correlacaoId, tentativa, null, inicio, cronometro, $"erro_{(int)resposta.StatusCode}");
                    await Task.Delay(TimeSpan.FromSeconds(tentativa), relogio, cancelamento);
                    continue;
                }

                resposta.EnsureSuccessStatusCode();
                var corpo = await resposta.Content.ReadFromJsonAsync<Resposta>(cancelamento)
                    ?? throw new InvalidOperationException("Resposta de embeddings vazia.");
                await RegistrarAsync(documentoId, correlacaoId, tentativa, corpo.Uso?.TokensEntrada, inicio, cronometro, "sucesso");
                return Validar(corpo, textos.Count);
            }
            catch (Exception excecao) when (excecao is HttpRequestException or TaskCanceledException && !cancelamento.IsCancellationRequested)
            {
                await RegistrarAsync(documentoId, correlacaoId, tentativa, null, inicio, cronometro, "erro");
                if (tentativa > opcoes.Value.Retentativas)
                {
                    throw;
                }

                LogRetentativa(logger, tentativa, excecao.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(tentativa), relogio, cancelamento);
            }
        }
    }

    /// <summary>Um vetor por texto, na ordem pedida, com a dimensão da coluna vector(1536).</summary>
    private List<float[]> Validar(Resposta corpo, int esperados)
    {
        var vetores = corpo.Dados.OrderBy(d => d.Indice).Select(d => d.Vetor).ToList();
        if (vetores.Count != esperados)
        {
            throw new InvalidOperationException($"A API devolveu {vetores.Count} embeddings para {esperados} textos.");
        }

        if (vetores.Any(v => v.Length != Dimensao))
        {
            throw new InvalidOperationException($"Embedding com dimensão diferente de {Dimensao}: confira o modelo configurado.");
        }

        return vetores;
    }

    private Task RegistrarAsync(Guid? documentoId, string correlacaoId, int tentativa, int? tokens, DateTimeOffset inicio, Stopwatch cronometro, string status) =>
        registrador.RegistrarAsync(
            new RegistroConsumoEmbeddings(documentoId, correlacaoId, Provedor, Modelo, tentativa, tokens, inicio, (int)cronometro.ElapsedMilliseconds, status),
            CancellationToken.None);

    private static bool Transitorio(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha transitória nos embeddings (tentativa {Tentativa}, {Tipo}); nova tentativa.")]
    private static partial void LogRetentativa(ILogger logger, int tentativa, string tipo);
}
