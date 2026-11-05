using System.Text;
using System.Text.Json;
using A2A;
using A2A.AspNetCore;
using Api.Agente.Application.Execucoes;
using Api.Agente.Application.Validacao;
using Api.Agente.Domain.Execucoes;
using Api.Agente.Endpoints.Infraestrutura;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Endpoints.Validacoes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Api.Agente.Endpoints.A2A;

/// <summary>
/// Skill de validação por A2A. Chama o mesmo caso de uso da API REST e devolve o mesmo envelope, sem caminho
/// que contorne a validação determinística (P-20, FR-081). A autorização é a mesma dos endpoints REST.
/// </summary>
public sealed class AgenteA2a(
    IHttpContextAccessor http,
    ValidarArquivoHandler handler,
    IRepositorioExecucoes repositorio,
    IOptions<JsonOptions> json) : IAgentHandler
{
    private const string NomeArquivoPadrao = "efd.txt";

    private static readonly TimeSpan IntervaloConsulta = TimeSpan.FromMilliseconds(200);

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var tarefa = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await tarefa.SubmitAsync(cancellationToken: cancellationToken);

        var contexto = http.HttpContext ?? throw new InvalidOperationException("A chamada A2A exige o contexto HTTP autenticado.");
        var parte = context.Message.Parts.FirstOrDefault(p => p.Raw is not null) ?? context.Message.Parts.FirstOrDefault(p => p.Text is not null);
        if (parte is null)
        {
            await tarefa.RejectAsync(Mensagem("Envie o arquivo EFD ICMS/IPI como arquivo ou texto na mensagem."), cancellationToken: cancellationToken);
            return;
        }

        Execucao recebida;
        try
        {
            // Partes de texto chegam decodificadas; a recodificação preserva o conteúdo em ISO 8859-1.
            await using var conteudo = new MemoryStream(parte.Raw ?? Encoding.Latin1.GetBytes(parte.Text!));
            recebida = await handler.ReceberAsync(
                new ComandoValidacao(
                    conteudo,
                    parte.Filename ?? NomeArquivoPadrao,
                    ValidacoesEndpoints.Organizacao(contexto.User),
                    ValidacoesEndpoints.Usuario(contexto.User),
                    (string)contexto.Items[CorrelacaoMiddleware.ChaveItens]!,
                    CanalExecucao.A2a),
                cancellationToken);
        }
        catch (Exception recusa) when (recusa is ArquivoRecusadoException or LimiteExecucoesException)
        {
            await tarefa.RejectAsync(Mensagem(recusa.Message), cancellationToken: cancellationToken);
            return;
        }

        await tarefa.StartWorkAsync(cancellationToken: cancellationToken);

        var final = await AguardarFimAsync(recebida, cancellationToken);
        var envelope = JsonSerializer.SerializeToElement(EnvelopeMapper.Mapear(final), json.Value.SerializerOptions);
        await tarefa.AddArtifactAsync([Part.FromData(envelope)], cancellationToken: cancellationToken);
        await tarefa.CompleteAsync(cancellationToken: cancellationToken);
    }

    public async Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken) =>
        await new TaskUpdater(eventQueue, context.TaskId, context.ContextId).CancelAsync(cancellationToken: cancellationToken);

    private async Task<Execucao> AguardarFimAsync(Execucao recebida, CancellationToken cancelamento)
    {
        while (true)
        {
            var atual = await repositorio.ObterAsync(recebida.OrgId, recebida.Id, cancelamento)
                ?? throw new InvalidOperationException($"A execução {recebida.Id} não foi encontrada.");
            if (atual.EstaFinalizada)
            {
                return atual;
            }

            await Task.Delay(IntervaloConsulta, cancelamento);
        }
    }

    private static Message Mensagem(string texto) => new()
    {
        Role = Role.Agent,
        MessageId = Guid.NewGuid().ToString("N"),
        Parts = [Part.FromText(texto)],
    };
}

public static class A2aExtensions
{
    private const string CaminhoA2a = "/a2a";
    private const string EsquemaBearer = "bearer";

    /// <summary>Só anuncia o que está implementado (P-20): a skill de consulta entra com a história 3.</summary>
    public static AgentCard CriarCartao(string urlBase) => new()
    {
        Name = "AgenteSpedFiscal",
        Description = "Pré-validação de arquivos EFD ICMS/IPI (SPED Fiscal, leiaute 020). Não assina, não transmite e não substitui o PVA.",
        Version = "1.0.0",
        SupportedInterfaces = [new AgentInterface { Url = $"{urlBase.TrimEnd('/')}{CaminhoA2a}", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }],
        Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false },
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["application/json"],
        SecuritySchemes = new()
        {
            [EsquemaBearer] = new SecurityScheme { HttpAuthSecurityScheme = new HttpAuthSecurityScheme { Scheme = "bearer", BearerFormat = "JWT" } },
        },
        SecurityRequirements = [new SecurityRequirement { Schemes = new() { [EsquemaBearer] = new StringList() } }],
        Skills =
        [
            new AgentSkill
            {
                Id = "validar-efd-icms-ipi",
                Name = "Validar EFD ICMS/IPI",
                Description = "Recebe um arquivo EFD ICMS/IPI (.txt, leiaute 020, períodos de 2026) e retorna o envelope de validação. A tarefa conclui quando o envelope atinge o status final.",
                Tags = ["sped", "efd", "icms", "ipi", "validacao"],
                InputModes = ["text/plain"],
                OutputModes = ["application/json"],
            },
        ],
    };

    public static IServiceCollection AddA2aAgente(this IServiceCollection services, IConfiguration configuracao)
    {
        services.AddHttpContextAccessor();
        services.AddA2AAgent<AgenteA2a>(CriarCartao(configuracao["A2a:UrlBase"] ?? "http://localhost:8080"));
        return services;
    }

    /// <summary>A constitution 5.1 exige autenticação em A2A: o Agent Card também é protegido.</summary>
    public static IEndpointRouteBuilder MapA2aAgente(this IEndpointRouteBuilder rotas, IConfiguration configuracao)
    {
        rotas.MapA2A(CaminhoA2a).RequireAuthorization(Politicas.AnalistaOuAgente);
        rotas.MapWellKnownAgentCard(CriarCartao(configuracao["A2a:UrlBase"] ?? "http://localhost:8080"), "/")
            .RequireAuthorization(Politicas.AnalistaOuAgente);
        return rotas;
    }
}
