using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Api.Rag.Endpoints.Infraestrutura;

/// <summary>
/// Documento OpenAPI gerado a partir dos endpoints e tela do Swagger, só em desenvolvimento
/// (research R-36). O contrato de referência continua em contracts/api-rag.openapi.yaml.
/// </summary>
public static class DocumentacaoApiExtensions
{
    private const string EsquemaBearer = "Bearer";

    public static IServiceCollection AddDocumentacaoApi(this IServiceCollection services) =>
        services.AddOpenApi(opcoes => opcoes.AddDocumentTransformer(AdicionarBearer));

    /// <summary>Chamar antes de UseAuthentication: a política padrão exige token em toda rota.</summary>
    public static WebApplication UseDocumentacaoApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.MapOpenApi().AllowAnonymous();
        app.UseSwaggerUI(opcoes =>
        {
            opcoes.SwaggerEndpoint("/openapi/v1.json", "Api.Rag v1");
            opcoes.DocumentTitle = "Api.Rag";
        });

        return app;
    }

    private static Task AdicionarBearer(
        OpenApiDocument documento, OpenApiDocumentTransformerContext contexto, CancellationToken cancelamento)
    {
        documento.Components ??= new OpenApiComponents();
        documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        documento.Components.SecuritySchemes[EsquemaBearer] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token gerado com dotnet user-jwts (ver quickstart.md).",
        };
        documento.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(EsquemaBearer, documento)] = [] },
        ];
        return Task.CompletedTask;
    }
}
