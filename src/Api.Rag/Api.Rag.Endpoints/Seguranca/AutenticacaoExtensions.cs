using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Api.Rag.Endpoints.Seguranca;

public static class Politicas
{
    public const string AdminBase = "admin_base";
    public const string ServicoMcp = "servico_mcp";
}

public static class AutenticacaoExtensions
{
    public const string AudienciaServico = "api-rag";

    /// <summary>
    /// JWT Bearer lido de Authentication:Schemes:Bearer. REST de administração exige o papel
    /// admin_base; o endpoint /mcp exige token de serviço com audiência api-rag (P-07).
    /// </summary>
    public static IServiceCollection AddAutenticacaoRag(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(Base().Build())
            .SetDefaultPolicy(Base().Build())
            .AddPolicy(Politicas.AdminBase, Base().RequireRole("admin_base").RequireClaim("org_id").Build())
            .AddPolicy(Politicas.ServicoMcp, Base().RequireClaim("aud", AudienciaServico).Build());

        return services;
    }

    private static AuthorizationPolicyBuilder Base() =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser();
}
