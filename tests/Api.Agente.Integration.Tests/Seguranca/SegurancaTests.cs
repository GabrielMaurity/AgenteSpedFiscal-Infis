using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Api.Agente.Infrastructure.Configuracao;

namespace Api.Agente.Integration.Tests.Seguranca;

public sealed class SegurancaTests : IClassFixture<FabricaApiAgente>
{
    private readonly FabricaApiAgente _fabrica;

    public SegurancaTests(FabricaApiAgente fabrica)
    {
        _fabrica = fabrica;
        _ = _fabrica.Server;
    }

    private async Task<bool> Autoriza(System.Security.Claims.ClaimsPrincipal usuario, string politica)
    {
        var autorizacao = _fabrica.Services.GetRequiredService<IAuthorizationService>();
        return (await autorizacao.AuthorizeAsync(usuario, politica)).Succeeded;
    }

    [Theory]
    [InlineData(Politicas.Analista, Papeis.Analista, true)]
    [InlineData(Politicas.Analista, Papeis.AdminBase, false)]
    [InlineData(Politicas.AdminBase, Papeis.AdminBase, true)]
    [InlineData(Politicas.AdminBase, Papeis.Analista, false)]
    [InlineData(Politicas.AnalistaOuAgente, Papeis.AgenteExterno, true)]
    [InlineData(Politicas.AnalistaOuAgente, Papeis.AdminBase, false)]
    [InlineData(Politicas.AnalistaOuAdmin, Papeis.AdminBase, true)]
    [InlineData(Politicas.AnalistaOuAdmin, Papeis.AgenteExterno, false)]
    public async Task Politicas_exigem_o_papel(string politica, string papel, bool esperado) =>
        Assert.Equal(esperado, await Autoriza(FabricaApiAgente.Usuario("org", papel), politica));

    [Theory]
    [InlineData(Politicas.Analista)]
    [InlineData(Politicas.AdminBase)]
    [InlineData(Politicas.AnalistaOuAgente)]
    public async Task Sem_org_id_nenhuma_politica_autoriza(string politica) =>
        Assert.False(await Autoriza(
            FabricaApiAgente.Usuario(null, Papeis.Analista, Papeis.AdminBase, Papeis.AgenteExterno), politica));

    [Fact]
    public async Task Endpoint_sem_politica_exige_usuario_autenticado_com_org_id()
    {
        var provedor = _fabrica.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provedor.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
        Assert.Contains(fallback.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.ClaimsAuthorizationRequirement c && c.ClaimType == "org_id");
    }

    private async Task<Microsoft.IdentityModel.Tokens.TokenValidationResult> ValidarToken(string token)
    {
        var opcoes = _fabrica.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        return await new JsonWebTokenHandler().ValidateTokenAsync(token, opcoes.TokenValidationParameters);
    }

    [Fact]
    public async Task Token_com_emissor_audiencia_e_chave_corretos_e_aceito()
    {
        var resultado = await ValidarToken(FabricaApiAgente.CriarToken(papeis: Papeis.Analista));

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public async Task Token_de_outra_audiencia_e_recusado()
    {
        var resultado = await ValidarToken(FabricaApiAgente.CriarToken(audiencia: "api-rag", papeis: Papeis.Analista));

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Limitador_respeita_o_maximo_por_organizacao_e_libera()
    {
        var limitador = new LimitadorExecucoesPorOrganizacao(Options.Create(new LimitesOpcoes { ExecucoesSimultaneasPorOrganizacao = 1 }));

        Assert.True(limitador.TentarReservar("a"));
        Assert.False(limitador.TentarReservar("a"));
        Assert.True(limitador.TentarReservar("b"));

        limitador.Liberar("a");

        Assert.True(limitador.TentarReservar("a"));
    }
}
