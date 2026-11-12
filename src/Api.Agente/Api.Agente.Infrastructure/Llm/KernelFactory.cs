using System.Text.Json.Nodes;
using Api.Agente.Application.Analise;
using Api.Agente.Domain.Efd;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Infrastructure.Llm.Ferramentas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;

namespace Api.Agente.Infrastructure.Llm;

/// <summary>
/// Kernel do Semantic Kernel por sessão: serviço de chat, ferramentas somente leitura e o filtro de
/// autorização com o estado da sessão (research R-09, R-11). Modelo, temperatura, limite de tokens e
/// timeout vêm de configuração; a saída usa JSON Schema estrito gerado dos contratos versionados.
/// </summary>
public sealed class KernelFactory(
    IChatCompletionService chat,
    IBaseInformacao baseInformacao,
    DadosEfd dados,
    IOptions<OpenAiOpcoes> openAi,
    IOptions<LimitesOpcoes> limites,
    IOptions<BaseInformacaoOpcoes> opcoesBase,
    ILoggerFactory logs)
{
    public const string NomePlugin = "efd";

    private static readonly Lazy<BinaryData> EsquemaAnalise = new(() => AdaptarParaOpenAi("llm-analise.schema.json"));
    private static readonly Lazy<BinaryData> EsquemaConversa = new(() => AdaptarParaOpenAi("llm-conversa.schema.json"));

    public string Modelo => openAi.Value.ModeloAnalise;

    public int LimiteChamadas => limites.Value.ChamadasFerramentaPorExecucao;

    public Kernel Criar(SessaoFerramentas sessao)
    {
        var construtor = Kernel.CreateBuilder();
        construtor.Services.AddSingleton(chat);
        var kernel = construtor.Build();

        kernel.Plugins.AddFromObject(new FerramentasAgente(sessao, baseInformacao, dados.Catalogo), NomePlugin);
        kernel.FunctionInvocationFilters.Add(new FiltroAutorizacaoFerramentas(
            sessao,
            limites.Value.ChamadasFerramentaPorExecucao,
            TimeSpan.FromSeconds(opcoesBase.Value.TimeoutFerramentaSegundos),
            logs.CreateLogger<FiltroAutorizacaoFerramentas>()));
        return kernel;
    }

    /// <param name="permitirFerramentas">Falso na rodada final, para obrigar a resposta no formato pedido.</param>
    public OpenAIPromptExecutionSettings Configuracoes(FormatoResposta formato, bool permitirFerramentas) => new()
    {
        ModelId = openAi.Value.ModeloAnalise,
        Temperature = openAi.Value.Temperatura,
        MaxTokens = openAi.Value.MaximoTokensSaida,
        ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            formato == FormatoResposta.Analise ? "analise_efd" : "resposta_conversa",
            formato == FormatoResposta.Analise ? EsquemaAnalise.Value : EsquemaConversa.Value,
            jsonSchemaIsStrict: true),

        // Sem invocação automática: o adaptador chama cada ferramenta, o que passa pelo filtro e permite
        // registrar o consumo de cada chamada ao modelo (P-17).
        FunctionChoiceBehavior = permitirFerramentas ? FunctionChoiceBehavior.Auto(autoInvoke: false) : FunctionChoiceBehavior.None(),
    };

    /// <summary>
    /// O modo estrito aceita um subconjunto do JSON Schema: sai a identificação do documento e o
    /// <c>const</c> vira <c>enum</c> de um valor; enums ganham o tipo explícito.
    /// </summary>
    public static BinaryData AdaptarParaOpenAi(string recurso)
    {
        using var fluxo = typeof(KernelFactory).Assembly.GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"Recurso {recurso} não encontrado.");
        var raiz = JsonNode.Parse(fluxo)!.AsObject();
        raiz.Remove("$schema");
        raiz.Remove("$id");
        raiz.Remove("title");
        Adaptar(raiz);
        return BinaryData.FromString(raiz.ToJsonString());
    }

    private static void Adaptar(JsonNode? no)
    {
        switch (no)
        {
            case JsonObject objeto:
                if (objeto["const"] is { } constante)
                {
                    objeto.Remove("const");
                    objeto["enum"] = new JsonArray(constante.DeepClone());
                }

                if (objeto["enum"] is JsonArray valores && objeto["type"] is null && valores.All(v => v?.GetValueKind() == System.Text.Json.JsonValueKind.String))
                {
                    objeto["type"] = "string";
                }

                foreach (var (_, filho) in objeto.ToList())
                {
                    Adaptar(filho);
                }

                break;

            case JsonArray lista:
                foreach (var item in lista)
                {
                    Adaptar(item);
                }

                break;
        }
    }
}
