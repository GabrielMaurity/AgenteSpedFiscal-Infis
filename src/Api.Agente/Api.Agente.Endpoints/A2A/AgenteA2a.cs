using System.Text;
using System.Text.Json;
using A2A;
using A2A.AspNetCore;
using Api.Agente.Application.Conversas;
using Api.Agente.Application.Execucoes;
using Api.Agente.Application.Validacao;
using Api.Agente.Domain.Execucoes;
using Api.Agente.Endpoints.Conversas;
using Api.Agente.Endpoints.Infraestrutura;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Endpoints.Validacoes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Api.Agente.Endpoints.A2A;

/// <summary>
/// Skills por A2A. Cada uma chama o mesmo caso de uso da API REST e devolve o mesmo formato, sem caminho
/// que contorne a validação determinística ou a fundamentação (P-20, FR-081). A autorização é a mesma dos
/// endpoints REST.
/// </summary>
public sealed class AgenteA2a(
    IHttpContextAccessor http,
    ValidarArquivoHandler handler,
    ConversasHandler conversas,
    IRepositorioExecucoes repositorio,
    IOptions<JsonOptions> json) : IAgentHandler
{
    public const string SkillValidar = "validar-efd-icms-ipi";
    public const string SkillConsultar = "consultar-efd-icms-ipi";

    private const string NomeArquivoPadrao = "efd.txt";

    private static readonly TimeSpan IntervaloConsulta = TimeSpan.FromMilliseconds(200);

    public async Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
    {
        var tarefa = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);
        await tarefa.SubmitAsync(cancellationToken: cancellationToken);

        var contexto = http.HttpContext ?? throw new InvalidOperationException("A chamada A2A exige o contexto HTTP autenticado.");
        if (PedidoConsulta(context.Message) is { } consulta)
        {
            await ConsultarAsync(contexto, consulta, tarefa, cancellationToken);
            return;
        }

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

    /// <summary>
    /// Consulta quando a mensagem traz uma parte de dados com <c>skill = consultar-efd-icms-ipi</c> e
    /// <c>pergunta</c> (opcionais: <c>conversaId</c>, <c>execucaoId</c>, <c>achadoId</c>, <c>periodo</c>).
    /// Sem ela, a mensagem é tratada como envio de arquivo para validação.
    /// </summary>
    public static JsonElement? PedidoConsulta(Message mensagem) =>
        mensagem.Parts
            .Select(p => p.Data)
            .OfType<JsonElement>()
            .Where(d => d.ValueKind == JsonValueKind.Object
                && d.TryGetProperty("skill", out var skill)
                && skill.ValueKind == JsonValueKind.String
                && skill.GetString() == SkillConsultar)
            .Cast<JsonElement?>()
            .FirstOrDefault();

    private async Task ConsultarAsync(HttpContext contexto, JsonElement pedido, TaskUpdater tarefa, CancellationToken cancelamento)
    {
        var orgId = ValidacoesEndpoints.Organizacao(contexto.User);
        var usuarioId = ValidacoesEndpoints.Usuario(contexto.User);
        try
        {
            var conversaId = Guid.TryParse(Texto(pedido, "conversaId"), out var existente)
                ? existente
                : (await conversas.CriarAsync(orgId, usuarioId, Guid.TryParse(Texto(pedido, "execucaoId"), out var execucao) ? execucao : null, cancelamento)).Id;

            await tarefa.StartWorkAsync(cancellationToken: cancelamento);
            var resposta = await conversas.EnviarAsync(
                new ComandoMensagem(
                    orgId,
                    usuarioId,
                    ConversasEndpoints.Papeis(contexto.User),
                    conversaId,
                    Texto(pedido, "pergunta") ?? "",
                    Texto(pedido, "achadoId"),
                    Texto(pedido, "periodo"),
                    (string)contexto.Items[CorrelacaoMiddleware.ChaveItens]!),
                cancelamento);

            var resultado = JsonSerializer.SerializeToElement(new { conversaId, resposta }, json.Value.SerializerOptions);
            await tarefa.AddArtifactAsync([Part.FromData(resultado)], cancellationToken: cancelamento);
            await tarefa.CompleteAsync(cancellationToken: cancelamento);
        }
        catch (Exception recusa) when (recusa is ConversaNaoEncontradaException or ExecucaoNaoEncontradaException or MensagemInvalidaException)
        {
            await tarefa.RejectAsync(Mensagem(recusa.Message), cancellationToken: cancelamento);
        }
    }

    private static string? Texto(JsonElement objeto, string campo) =>
        objeto.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

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

    /// <summary>Só anuncia o que está implementado (P-20).</summary>
    public static AgentCard CriarCartao(string urlBase) => new()
    {
        Name = "AgenteSpedFiscal",
        Description = "Pré-validação de arquivos EFD ICMS/IPI (SPED Fiscal, leiaute 020) e consulta fundamentada no Guia Prático oficial. Não assina, não transmite e não substitui o PVA.",
        Version = "1.0.0",
        SupportedInterfaces = [new AgentInterface { Url = $"{urlBase.TrimEnd('/')}{CaminhoA2a}", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }],
        Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false },
        DefaultInputModes = ["text/plain", "application/json"],
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
                Id = AgenteA2a.SkillValidar,
                Name = "Validar EFD ICMS/IPI",
                Description = "Recebe um arquivo EFD ICMS/IPI (.txt, leiaute 020, períodos de 2026) e retorna o envelope de validação. A tarefa conclui quando o envelope atinge o status final.",
                Tags = ["sped", "efd", "icms", "ipi", "validacao"],
                InputModes = ["text/plain"],
                OutputModes = ["application/json"],
            },
            new AgentSkill
            {
                Id = AgenteA2a.SkillConsultar,
                Name = "Consultar orientações da EFD ICMS/IPI",
                Description = "Responde perguntas sobre a EFD ICMS/IPI com referências à base de informação, indicando se cada fonte é oficial ou material interno da Infis, ou declara a ausência de evidências. Envie uma parte de dados com skill = consultar-efd-icms-ipi e pergunta.",
                Tags = ["sped", "efd", "consulta", "rag"],
                InputModes = ["application/json"],
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
