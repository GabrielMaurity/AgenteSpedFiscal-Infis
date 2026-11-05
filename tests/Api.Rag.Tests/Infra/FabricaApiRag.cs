using System.Security.Claims;
using Api.Rag.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Api.Rag.Tests.Infra;

/// <summary>API real apontando para um banco inexistente, sem inicializá-lo.</summary>
public sealed class FabricaApiRag : WebApplicationFactory<Program>
{
    public IReadOnlyDictionary<string, string?> Configuracao { get; init; } = ConfiguracaoPadrao();

    public static Dictionary<string, string?> ConfiguracaoPadrao() => new()
    {
        ["OpenAi:ApiKey"] = "chave-falsa",
        ["Postgres:ConnectionString"] = "Host=localhost;Port=1;Database=testes;Timeout=1",
        ["Authentication:Schemes:Bearer:ValidIssuer"] = "testes",
        ["Authentication:Schemes:Bearer:ValidAudiences:0"] = "api-rag",
    };

    public static ClaimsPrincipal Usuario(string? audiencia, string? orgId, params string[] papeis)
    {
        var claims = papeis.Select(p => new Claim(ClaimTypes.Role, p)).ToList();
        if (audiencia is not null)
        {
            claims.Add(new Claim("aud", audiencia));
        }

        if (orgId is not null)
        {
            claims.Add(new Claim("org_id", orgId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        foreach (var (chave, valor) in Configuracao)
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureTestServices(servicos => servicos.Remove(servicos.Single(s =>
            s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(InicializadorBanco))));
    }
}
