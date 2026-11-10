using System.Security.Claims;
using System.Text;
using Api.Rag.Application.Base;
using Api.Rag.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Rag.Tests.Infra;

/// <summary>API real com JWT de teste. Por padrão, aponta para um banco inexistente e não o inicializa.</summary>
public sealed class FabricaApiRag : WebApplicationFactory<Program>
{
    public const string Emissor = "testes";
    public const string Audiencia = "api-rag";

    private static readonly byte[] Chave = Encoding.UTF8.GetBytes("chave-de-testes-rag-com-mais-de-32-bytes-0123456789");

    public IReadOnlyDictionary<string, string?> Configuracao { get; init; } = ConfiguracaoPadrao();

    /// <summary>Substitui o gerador da OpenAI; os testes nunca chamam a API real.</summary>
    public IGeradorEmbeddings? Embeddings { get; init; }

    public static Dictionary<string, string?> ConfiguracaoPadrao() => new()
    {
        ["OpenAi:ApiKey"] = "chave-falsa",
        ["Postgres:ConnectionString"] = "Host=localhost;Port=1;Database=testes;Timeout=1",
        ["Authentication:Schemes:Bearer:ValidIssuer"] = Emissor,
        ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Audiencia,
        ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = Emissor,
        ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(Chave),
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

    public static string CriarToken(string? orgId = "org-teste", string audiencia = Audiencia, params string[] papeis)
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
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Chave), SecurityAlgorithms.HmacSha256),
        });
    }

    public HttpClient Cliente(string? token)
    {
        var cliente = CreateClient();
        if (token is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return cliente;
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
            servicos.Remove(servicos.Single(s =>
                s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(InicializadorBanco)));
            if (Embeddings is not null)
            {
                servicos.RemoveAll<IGeradorEmbeddings>();
                servicos.AddSingleton(Embeddings);
            }
        });
    }
}
