// Avaliação de RAG e LLM sob demanda (research R-17, constitution 5.2). Fora da suíte de PR (P-22).
//
// Uso:
//   dotnet run --project tests/AgenteSpedFiscal.Evals -- --conjunto evals/rag-guia-3.2.4
//     [--rag http://localhost:8081/mcp] [--agente http://localhost:8080] [--k 8] [--saida evals/resultados]
//     [--somente recuperacao|conversas]
//
// Tokens por variável de ambiente (nunca no repositório, P-19):
//   EVALS_TOKEN_RAG     audiência api-rag (dotnet user-jwts create ... --audience api-rag)
//   EVALS_TOKEN_AGENTE  audiência api-agente, papel analista e claim org_id
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgenteSpedFiscal.Contracts.Mcp;
using AgenteSpedFiscal.Evals;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var argumentos = Argumentos(args);
var diretorio = argumentos.GetValueOrDefault("conjunto") ?? "evals/rag-guia-3.2.4";
var urlRag = new Uri(argumentos.GetValueOrDefault("rag") ?? "http://localhost:8081/mcp");
var urlAgente = new Uri(argumentos.GetValueOrDefault("agente") ?? "http://localhost:8080");
var k = int.Parse(argumentos.GetValueOrDefault("k") ?? "8", CultureInfo.InvariantCulture);
var saida = argumentos.GetValueOrDefault("saida") ?? "evals/resultados";
var somente = argumentos.GetValueOrDefault("somente");
var cancelamento = CancellationToken.None;
string[] somenteOficial = ["oficial"];

var relatorio = new Dictionary<string, object?>
{
    ["conjunto"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(diretorio)),
    ["executadoEm"] = DateTimeOffset.UtcNow,
    ["topK"] = k,
};

if (somente is null or "recuperacao")
{
    var token = Environment.GetEnvironmentVariable("EVALS_TOKEN_RAG") ?? throw new InvalidOperationException("Defina EVALS_TOKEN_RAG.");
    var conjunto = Conjuntos.Ler<ItemRecuperacao>(diretorio, "recuperacao");
    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    var transporte = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = urlRag, TransportMode = HttpTransportMode.StreamableHttp }, http, NullLoggerFactory.Instance);
    await using var mcp = await McpClient.CreateAsync(transporte, cancellationToken: cancelamento);

    var resultados = new List<ResultadoRecuperacao>();
    foreach (var item in conjunto.Itens)
    {
        var chamada = await mcp.CallToolAsync(
            ContratoMcp.BuscarTrechos,
            new Dictionary<string, object?>
            {
                ["consulta"] = item.Pergunta,
                ["tipoDocumento"] = "guia_pratico_efd_icms_ipi",
                ["correlacaoId"] = $"evals-{item.Id}",
                ["periodo"] = item.Periodo,
                ["classificacoes"] = somenteOficial,
                ["topK"] = k,
            },
            cancellationToken: cancelamento);
        if (chamada.IsError == true)
        {
            throw new InvalidOperationException($"{item.Id}: erro da ferramenta: {string.Concat(chamada.Content.OfType<TextContentBlock>().Select(c => c.Text))}");
        }

        var resposta = chamada.StructuredContent?.Deserialize<BuscarTrechosResponse>(Conjuntos.Json)
            ?? throw new InvalidOperationException($"{item.Id}: resposta MCP sem conteúdo estruturado.");
        var posicao = Metricas.PrimeiraPosicaoRelevante(resposta.Trechos, item.Esperado);
        resultados.Add(new ResultadoRecuperacao(item.Id, posicao, resposta.Trechos.Count));
        Console.WriteLine($"[recuperacao] {item.Id}: {(posicao is { } p ? $"relevante na posição {p}" : "sem trecho relevante")}");
    }

    relatorio["recuperacao"] = new
    {
        recallEmK = Metricas.RecallEmK(resultados),
        mrr = Metricas.Mrr(resultados),
        itens = resultados,
    };
}

if (somente is null or "conversas")
{
    var token = Environment.GetEnvironmentVariable("EVALS_TOKEN_AGENTE") ?? throw new InvalidOperationException("Defina EVALS_TOKEN_AGENTE.");
    using var http = new HttpClient { BaseAddress = urlAgente, Timeout = TimeSpan.FromMinutes(3) };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var resultados = new List<ResultadoConversa>();
    foreach (var nome in Conjuntos.Conversas)
    {
        foreach (var item in Conjuntos.Ler<ItemConversa>(diretorio, nome).Itens)
        {
            // Uma conversa por pergunta: o histórico não interfere na medição.
            var criada = await http.PostAsJsonAsync("/api/v1/conversas", new { }, cancelamento);
            criada.EnsureSuccessStatusCode();
            var conversaId = (await criada.Content.ReadFromJsonAsync<JsonElement>(cancelamento)).GetProperty("conversaId").GetString();

            var cronometro = Stopwatch.StartNew();
            var envio = await http.PostAsJsonAsync($"/api/v1/conversas/{conversaId}/mensagens", new { texto = item.Pergunta, periodo = item.Periodo }, cancelamento);
            List<string> falhas;
            var fundamentada = false;
            if (envio.IsSuccessStatusCode)
            {
                var resposta = await envio.Content.ReadFromJsonAsync<JsonElement>(cancelamento);
                fundamentada = resposta.GetProperty("fundamentada").GetBoolean();
                falhas = Metricas.Avaliar(resposta, item.Criterios);
            }
            else
            {
                falhas = [$"HTTP {(int)envio.StatusCode}"];
            }

            resultados.Add(new ResultadoConversa(nome, item.Id, falhas.Count == 0, fundamentada, falhas));
            Console.WriteLine($"[{nome}] {item.Id}: {(falhas.Count == 0 ? "ok" : string.Join("; ", falhas))} ({cronometro.ElapsedMilliseconds} ms)");
        }
    }

    relatorio["conversas"] = resultados
        .GroupBy(r => r.Conjunto)
        .ToDictionary(
            g => g.Key,
            g => (object)new
            {
                taxaAcerto = g.Count(r => r.Aprovado) / (double)g.Count(),
                taxaLimitacao = g.Count(r => !r.Fundamentada) / (double)g.Count(),
                itens = g.ToList(),
            });
}

Directory.CreateDirectory(saida);
var carimbo = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var caminho = Path.Combine(saida, $"{relatorio["conjunto"]}-{carimbo}.json");
await File.WriteAllTextAsync(caminho, JsonSerializer.Serialize(relatorio, Conjuntos.Json), Encoding.UTF8, cancelamento);
Console.WriteLine($"Resultados gravados em {caminho}");
return 0;

static Dictionary<string, string> Argumentos(string[] args)
{
    var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
        {
            resultado[args[i][2..]] = args[++i];
        }
    }

    return resultado;
}
