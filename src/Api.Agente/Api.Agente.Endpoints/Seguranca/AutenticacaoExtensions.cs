using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Api.Agente.Endpoints.Seguranca;

public static class Politicas
{
    public const string Analista = "analista";
    public const string AdminBase = "admin_base";
    public const string AnalistaOuAgente = "analista_ou_agente";
    public const string AnalistaOuAdmin = "analista_ou_admin";
}

public static class Papeis
{
    public const string Analista = "analista";
    public const string AdminBase = "admin_base";
    public const string AgenteExterno = "agente_externo";
}

public static class AutenticacaoExtensions
{
    public const string ClaimOrganizacao = "org_id";

    /// <summary>
    /// JWT Bearer lido de Authentication:Schemes:Bearer. Por padrão, todo endpoint exige usuário
    /// autenticado com org_id (FR-080); as exceções declaram AllowAnonymous.
    /// </summary>
    public static IServiceCollection AddAutenticacaoAgente(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(Base().Build())
            .SetDefaultPolicy(Base().Build())
            .AddPolicy(Politicas.Analista, Base().RequireRole(Papeis.Analista).Build())
            .AddPolicy(Politicas.AdminBase, Base().RequireRole(Papeis.AdminBase).Build())
            .AddPolicy(Politicas.AnalistaOuAgente, Base().RequireRole(Papeis.Analista, Papeis.AgenteExterno).Build())
            .AddPolicy(Politicas.AnalistaOuAdmin, Base().RequireRole(Papeis.Analista, Papeis.AdminBase).Build());

        return services;
    }

    private static AuthorizationPolicyBuilder Base() =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(ClaimOrganizacao);
}
