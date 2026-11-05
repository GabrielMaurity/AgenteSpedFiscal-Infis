using Api.Agente.Domain.Efd;
using Api.Agente.Domain.Efd.Catalogo;
using Api.Agente.Endpoints.Seguranca;

namespace Api.Agente.Endpoints.Regras;

/// <summary>Consulta somente leitura do Catálogo de Regras (contrato api-agente.openapi.yaml).</summary>
public static class RegrasEndpoints
{
    private static readonly string[] StatusValidos = ["ativa", "pendente_confirmacao", "revogada"];

    public static IEndpointRouteBuilder MapRegras(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/api/v1/regras", Listar).RequireAuthorization(Politicas.AnalistaOuAdmin);
        return rotas;
    }

    private static IResult Listar(DadosEfd dados, string? status = null)
    {
        if (status is not null && !StatusValidos.Contains(status))
        {
            return Results.Problem(
                detail: $"Use um destes status: {string.Join(", ", StatusValidos)}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Status inválido");
        }

        return Results.Ok(dados.Catalogo.Regras
            .Where(r => status is null || r.Status == status)
            .Select(Mapear));
    }

    /// <summary>Só os campos do schema catalogo-regras: as propriedades calculadas da regra ficam de fora.</summary>
    private static object Mapear(RegraCatalogo regra) => new
    {
        id = regra.Id,
        descricao = regra.Descricao,
        tipo = regra.Tipo,
        severidade = regra.Severidade,
        status = regra.Status,
        aplicacao = regra.Aplicacao,
        fonte = regra.Fonte,
        origemCandidata = regra.OrigemCandidata,
        dependeDe = regra.DependeDe,
        acaoCorretiva = regra.AcaoCorretiva,
    };
}
