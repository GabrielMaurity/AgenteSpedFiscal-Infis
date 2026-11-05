using System.Security.Claims;
using System.Text;
using Api.Agente.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Agente.Integration.Tests.Infra;

/// <summary>API real com JWT de teste. Por padrão, aponta para bancos inexistentes e não os inicializa.</summary>
public sealed class FabricaApiAgente : WebApplicationFactory<Program>
{
    public IReadOnlyDictionary<string, string?> Configuracao { get; init; } = ConfiguracaoPadrao();

    public const string Emissor = "testes";
    public const string Audiencia = "api-agente";

    private static readonly byte[] Chave = Encoding.UTF8.GetBytes("chave-de-testes-com-mais-de-32-bytes-0123456789");

    public static Dictionary<string, string?> ConfiguracaoPadrao() => new()
    {
        ["OpenAi:ApiKey"] = "chave-falsa",
        ["OpenAi:ModeloAnalise"] = "modelo-falso",
        ["MongoDb:ConnectionString"] = "mongodb://localhost:1/?serverSelectionTimeoutMS=200",
        ["MongoDb:Database"] = "testes",
        ["Postgres:ConnectionString"] = "Host=localhost;Port=1;Database=testes;Timeout=1",
        ["McpRag:Url"] = "http://localhost:1/mcp",
        ["Authentication:Schemes:Bearer:ValidIssuer"] = Emissor,
        ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Audiencia,
        ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = Emissor,
        ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(Chave),
    };

    public static string CriarToken(string? orgId = "org-teste", string? audiencia = Audiencia, params string[] papeis)
    {
        var claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = "usuario-teste" };
        if (orgId is not null)
        {
            claims["org_id"] = orgId;
        }

        if (papeis.Length > 0)
        {
            claims["role"] = papeis;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Emissor,
            Audience = audiencia,
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Chave), SecurityAlgorithms.HmacSha256),
        });
    }

    public static ClaimsPrincipal Usuario(string? orgId, params string[] papeis)
    {
        var claims = papeis.Select(p => new Claim(ClaimTypes.Role, p)).ToList();
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

        builder.ConfigureTestServices(servicos =>
        {
            var inicializador = servicos.Single(s => s.ServiceType == typeof(IHostedService)
                && s.ImplementationType == typeof(InicializadorBancos));
            servicos.Remove(inicializador);
        });
    }
}
