using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.Agente.Endpoints.Infraestrutura;

/// <summary>
/// Documento OpenAPI gerado a partir dos endpoints e tela do Swagger (research R-36). Ligados por padrão
/// só em desenvolvimento; fora dele, apenas com DocumentacaoApi:Habilitada = true (research R-40). A
/// documentação é pública, mas os endpoints continuam exigindo token. O contrato de referência continua
/// em contracts/api-agente.openapi.yaml.
/// </summary>
public static class DocumentacaoApiExtensions
{
    public const string ChaveHabilitada = "DocumentacaoApi:Habilitada";

    private const string EsquemaBearer = "Bearer";

    public static IServiceCollection AddDocumentacaoApi(this IServiceCollection services) =>
        services.AddOpenApi(opcoes => opcoes.AddDocumentTransformer(AjustarDocumento));

    public static bool Habilitada(IConfiguration configuracao, IHostEnvironment ambiente) =>
        configuracao.GetValue<bool?>(ChaveHabilitada) ?? ambiente.IsDevelopment();

    /// <summary>Chamar antes de UseAuthentication: a política padrão exige token em toda rota.</summary>
    public static WebApplication UseDocumentacaoApi(this WebApplication app)
    {
        if (!Habilitada(app.Configuration, app.Environment))
        {
            return app;
        }

        var titulo = app.Environment.IsDevelopment() ? "Api.Agente" : $"Api.Agente ({app.Environment.EnvironmentName})";
        app.MapOpenApi().AllowAnonymous();
        app.UseSwaggerUI(opcoes =>
        {
            opcoes.SwaggerEndpoint("/openapi/v1.json", "Api.Agente v1");
            opcoes.DocumentTitle = titulo;
            // O token colado em Authorize sobrevive ao recarregar a página (fica no navegador de quem usa).
            opcoes.EnablePersistAuthorization();
        });

        return app;
    }

    private static Task AjustarDocumento(
        OpenApiDocument documento, OpenApiDocumentTransformerContext contexto, CancellationToken cancelamento)
    {
        // Sem "servers", o Swagger chama a origem da própria página. Atrás do proxy do Vercel, o endereço
        // visto pela aplicação seria http e o navegador bloquearia as chamadas (conteúdo misto).
        documento.Servers = [];
        documento.Components ??= new OpenApiComponents();
        documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        documento.Components.SecuritySchemes[EsquemaBearer] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token JWT. Em desenvolvimento: dotnet user-jwts (quickstart.md). Em produção: token emitido pelo administrador.",
        };
        documento.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(EsquemaBearer, documento)] = [] },
        ];
        return Task.CompletedTask;
    }
}
