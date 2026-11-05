using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Api.Agente.Endpoints.Seguranca;
using Api.Agente.Integration.Tests.Infra;

namespace Api.Agente.Integration.Tests.TabelasReferencia;

/// <summary>Autorização e catálogo de regras, que não usam os bancos.</summary>
public sealed class NcmERegrasAutorizacaoTests(FabricaApiAgente fabrica) : IClassFixture<FabricaApiAgente>
{
    private static readonly Uri RotaNcm = new("/api/v1/tabelas-referencia/ncm", UriKind.Relative);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private HttpClient Cliente(string? papel = null)
    {
        var cliente = fabrica.CreateClient();
        if (papel is not null)
        {
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", FabricaApiAgente.CriarToken(papeis: papel));
        }

        return cliente;
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Papeis.Analista, HttpStatusCode.Forbidden)]
    [InlineData(Papeis.AgenteExterno, HttpStatusCode.Forbidden)]
    public async Task Importar_tabela_exige_o_papel_admin_base(string? papel, HttpStatusCode esperado)
    {
        var resposta = await Cliente(papel).PostAsync(RotaNcm, new MultipartFormDataContent(), Cancelamento);

        Assert.Equal(esperado, resposta.StatusCode);
    }

    [Fact]
    public async Task Listar_tabelas_e_negado_ao_agente_externo()
    {
        var resposta = await Cliente(Papeis.AgenteExterno).GetAsync(RotaNcm, Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Importar_sem_campos_obrigatorios_retorna_400()
    {
        using var corpo = new MultipartFormDataContent { { new StringContent("x"), "origem" } };

        var resposta = await Cliente(Papeis.AdminBase).PostAsync(RotaNcm, corpo, Cancelamento);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Regras_filtra_por_status_e_segue_o_formato_do_catalogo()
    {
        var resposta = await Cliente(Papeis.Analista).GetStringAsync(new Uri("/api/v1/regras?status=pendente_confirmacao", UriKind.Relative), Cancelamento);

        using var regras = JsonDocument.Parse(resposta);
        var ids = regras.RootElement.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        Assert.Contains("EFD-NEG-008", ids);
        Assert.DoesNotContain("EFD-ESTR-001", ids);
        Assert.All(regras.RootElement.EnumerateArray(), r =>
        {
            Assert.Equal("pendente_confirmacao", r.GetProperty("status").GetString());
            Assert.False(r.TryGetProperty("estaAtiva", out _));
            Assert.False(r.TryGetProperty("categoria", out _));
        });
    }

    [Fact]
    public async Task Regras_sem_filtro_traz_regra_ativa_com_fonte()
    {
        var resposta = await Cliente(Papeis.AdminBase).GetStringAsync(new Uri("/api/v1/regras", UriKind.Relative), Cancelamento);

        using var regras = JsonDocument.Parse(resposta);
        var regra = regras.RootElement.EnumerateArray().Single(r => r.GetProperty("id").GetString() == "EFD-NEG-001");
        Assert.Equal("negocio", regra.GetProperty("tipo").GetString());
        Assert.Equal("3.2.4", regra.GetProperty("fonte").GetProperty("versao").GetString());
        Assert.Equal("2026-01", regra.GetProperty("aplicacao").GetProperty("periodoInicio").GetString());
    }

    [Fact]
    public async Task Regras_com_status_invalido_retorna_400()
    {
        var resposta = await Cliente(Papeis.Analista).GetAsync(new Uri("/api/v1/regras?status=outro", UriKind.Relative), Cancelamento);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Regras_sao_negadas_ao_agente_externo()
    {
        var resposta = await Cliente(Papeis.AgenteExterno).GetAsync(new Uri("/api/v1/regras", UriKind.Relative), Cancelamento);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }
}
