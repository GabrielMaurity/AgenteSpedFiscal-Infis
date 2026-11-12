using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Api.Rag.Endpoints.Seguranca;

/// <summary>
/// Modo sem autenticação para desenvolvimento local (Autenticacao:Desabilitada = true). Toda requisição entra
/// como um usuário local com todos os papéis e o org_id fixo, então as políticas continuam valendo, mas sem
/// token. Só funciona no ambiente Development: em qualquer outro, a API se recusa a iniciar (FR-062).
/// </summary>
public sealed class AutenticacaoDesenvolvimentoHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> opcoes,
    ILoggerFactory logs,
    UrlEncoder codificador) : AuthenticationHandler<AuthenticationSchemeOptions>(opcoes, logs, codificador)
{
    public const string ChaveConfiguracao = "Autenticacao:Desabilitada";
    public const string UsuarioLocal = "usuario-local";
    public const string OrganizacaoLocal = "org-local";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, UsuarioLocal),
            new("sub", UsuarioLocal),
            new("org_id", OrganizacaoLocal),
            new(ClaimTypes.Role, "admin_base"),

            // Audiência de serviço: a Api.Agente local chama o /mcp sem token.
            new("aud", AutenticacaoExtensions.AudienciaServico),
        ];
        var usuario = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(usuario, Scheme.Name)));
    }

    /// <summary>Lê a chave e impede o uso fora de Development.</summary>
    public static bool Ativa(IConfiguration configuracao, IHostEnvironment ambiente)
    {
        if (!configuracao.GetValue<bool>(ChaveConfiguracao))
        {
            return false;
        }

        return ambiente.IsDevelopment()
            ? true
            : throw new InvalidOperationException($"{ChaveConfiguracao} só pode ser usada no ambiente Development.");
    }
}
