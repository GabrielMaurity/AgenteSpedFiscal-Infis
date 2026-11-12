using Api.Agente.Application.Analise;
using Api.Agente.Application.Conversas;
using Api.Agente.Infrastructure.Configuracao;
using Api.Agente.Infrastructure.Mcp;
using Api.Agente.Infrastructure.Resiliencia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Api.Agente.Infrastructure.Llm;

public static class AnaliseExtensions
{
    /// <summary>Base de informação por MCP, LLM pelo Semantic Kernel e casos de uso das etapas 5 a 8 e das conversas.</summary>
    public static IServiceCollection AddAnaliseIa(this IServiceCollection services)
    {
        services.AddSingleton<IBaseInformacao, ClienteMcpRag>();

        // HttpClient com a resiliência de research R-22. A chave vem da configuração, nunca do código (P-19).
        services.AddSingleton<IChatCompletionService>(provedor =>
        {
            var opcoes = provedor.GetRequiredService<IOptions<OpenAiOpcoes>>().Value;
            return new OpenAIChatCompletionService(
                opcoes.ModeloAnalise,
                opcoes.ApiKey,
                httpClient: provedor.GetRequiredService<IHttpClientFactory>().CreateClient(ClientesHttp.OpenAi));
        });

        services.AddSingleton<KernelFactory>();
        services.AddSingleton<IModeloAnalise, ModeloAnaliseSemanticKernel>();
        services.AddSingleton(provedor =>
        {
            var limites = provedor.GetRequiredService<IOptions<LimitesOpcoes>>().Value;
            var baseInformacao = provedor.GetRequiredService<IOptions<BaseInformacaoOpcoes>>().Value;
            return new ParametrosAnalise(
                limites.RetentativasRespostaLlm,
                baseInformacao.ConsultasPorExecucao,
                limites.MensagensEnviadasAoModelo,
                baseInformacao.TopK);
        });
        services.AddSingleton<AnalisadorExecucao>();
        services.AddSingleton<ConversasHandler>();
        return services;
    }
}
