using System.Security.Claims;
using Api.Agente.Application.Conversas;
using Api.Agente.Application.Validacao;
using Api.Agente.Domain.Conversas;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Endpoints.Validacoes;

namespace Api.Agente.Endpoints.Conversas;

public sealed record CriarConversaRequest(Guid? ExecucaoId);

public sealed record EnviarMensagemRequest(string? Texto, string? AchadoId, string? Periodo);

public sealed record MensagemHistorico(
    string MensagemId,
    string Papel,
    string Texto,
    bool Fundamentada,
    bool FundamentadaEmFonteOficial,
    double? Proximidade,
    IReadOnlyList<ReferenciaEnvelope> Referencias,
    IReadOnlyList<string> Limitacoes,
    string? AchadoId,
    DateTimeOffset CriadoEm);

/// <summary>Conversas do contrato api-agente.openapi.yaml (FR-050 a FR-054).</summary>
public static class ConversasEndpoints
{
    public static IEndpointRouteBuilder MapConversas(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/api/v1/conversas");

        grupo.MapPost("/", CriarAsync).RequireAuthorization(Politicas.AnalistaOuAgente);
        grupo.MapPost("/{conversaId:guid}/mensagens", EnviarAsync)
            .RequireAuthorization(Politicas.AnalistaOuAgente)
            .RequireRateLimiting(LimitacaoRequisicoesExtensions.PoliticaPorOrganizacao);
        grupo.MapGet("/{conversaId:guid}/mensagens", ListarAsync).RequireAuthorization(Politicas.Analista);

        return rotas;
    }

    public static IReadOnlyCollection<string> Papeis(ClaimsPrincipal usuario) =>
        [.. usuario.FindAll(ClaimTypes.Role).Select(c => c.Value)];

    private static async Task<IResult> CriarAsync(
        HttpContext contexto, ConversasHandler handler, CriarConversaRequest? corpo, CancellationToken cancelamento)
    {
        try
        {
            var conversa = await handler.CriarAsync(
                ValidacoesEndpoints.Organizacao(contexto.User), ValidacoesEndpoints.Usuario(contexto.User), corpo?.ExecucaoId, cancelamento);
            return Results.Created($"/api/v1/conversas/{conversa.Id}", new { conversaId = conversa.Id });
        }
        catch (ExecucaoNaoEncontradaException)
        {
            return Problema(StatusCodes.Status404NotFound, "Execução não encontrada", "A execução não existe, expirou ou pertence a outra organização.");
        }
    }

    private static async Task<IResult> EnviarAsync(
        HttpContext contexto, Guid conversaId, EnviarMensagemRequest corpo, ConversasHandler handler, CancellationToken cancelamento)
    {
        try
        {
            var resposta = await handler.EnviarAsync(
                new ComandoMensagem(
                    ValidacoesEndpoints.Organizacao(contexto.User),
                    ValidacoesEndpoints.Usuario(contexto.User),
                    Papeis(contexto.User),
                    conversaId,
                    corpo.Texto ?? "",
                    corpo.AchadoId,
                    corpo.Periodo,
                    ValidacoesEndpoints.Correlacao(contexto)),
                cancelamento);
            return Results.Ok(resposta);
        }
        catch (ConversaNaoEncontradaException)
        {
            return ConversaNaoEncontrada();
        }
        catch (MensagemInvalidaException invalida)
        {
            return Problema(StatusCodes.Status400BadRequest, "Mensagem inválida", invalida.Message);
        }
    }

    private static async Task<IResult> ListarAsync(HttpContext contexto, Guid conversaId, ConversasHandler handler, CancellationToken cancelamento)
    {
        try
        {
            var mensagens = await handler.ListarAsync(
                ValidacoesEndpoints.Organizacao(contexto.User), ValidacoesEndpoints.Usuario(contexto.User), conversaId, cancelamento);
            return Results.Ok(mensagens.Select(m => new MensagemHistorico(
                m.Id.ToString(),
                m.Papel == PapelMensagem.Usuario ? "usuario" : "agente",
                m.Texto,
                m.Fundamentada,
                m.FundamentadaEmFonteOficial,
                m.Proximidade,
                m.Referencias.Select(EnvelopeMapper.MapearReferencia).ToList(),
                m.Limitacoes,
                m.AchadoId,
                m.CriadoEm)));
        }
        catch (ConversaNaoEncontradaException)
        {
            return ConversaNaoEncontrada();
        }
    }

    // Conversa de outro usuário ou organização responde 404, sem revelar que existe (FR-054).
    private static IResult ConversaNaoEncontrada() =>
        Problema(StatusCodes.Status404NotFound, "Conversa não encontrada", "A conversa não existe, expirou ou pertence a outro usuário.");

    private static IResult Problema(int status, string titulo, string detalhe) =>
        Results.Problem(detail: detalhe, statusCode: status, title: titulo);
}
