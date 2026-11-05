using Api.Agente.Application.Consumo;
using Api.Agente.Infrastructure.Consumo;
using Api.Agente.Infrastructure.Persistencia.Postgres;

namespace Api.Agente.Integration.Tests.Persistencia;

public class ConsumoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly TabelaPreco Preco = new()
    {
        Id = Guid.NewGuid(),
        Provedor = "openai",
        Modelo = "modelo",
        PrecoEntradaPorMilhao = 1m,
        PrecoSaidaPorMilhao = 2m,
        Moeda = "USD",
        VigenteDesde = new DateOnly(2026, 1, 1),
        Referencia = "https://exemplo",
        VersaoArquivo = "1.0.0",
    };

    private static RegistroConsumo Registro(int? entrada, int? saida, int tentativa = 1) =>
        new(Guid.NewGuid(), "correlacao", "org", "openai", "modelo", "chat", tentativa, entrada, saida, Inicio, 100, "sucesso");

    [Fact]
    public void Tokens_informados_e_preco_conhecido_geram_custo_estimado()
    {
        var consumo = RegistradorConsumo.Montar(Registro(1_000_000, 500_000), Preco);

        Assert.Equal(OrigemTokens.Informado, consumo.OrigemTokens);
        Assert.Equal(2m, consumo.CustoEstimado);
        Assert.Equal(Preco.Id, consumo.TabelaPrecosId);
        Assert.Equal("USD", consumo.Moeda);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(100, null)]
    [InlineData(null, 100)]
    public void Consumo_desconhecido_fica_nulo_e_nunca_zero(int? entrada, int? saida)
    {
        var consumo = RegistradorConsumo.Montar(Registro(entrada, saida), Preco);

        Assert.Equal(OrigemTokens.Desconhecido, consumo.OrigemTokens);
        Assert.Equal(entrada, consumo.TokensEntrada);
        Assert.Equal(saida, consumo.TokensSaida);
        Assert.Null(consumo.CustoEstimado);
        Assert.Null(consumo.TabelaPrecosId);
    }

    [Fact]
    public void Sem_preco_os_tokens_ficam_e_o_custo_nao_e_inventado()
    {
        var consumo = RegistradorConsumo.Montar(Registro(10, 10), null);

        Assert.Equal(OrigemTokens.Informado, consumo.OrigemTokens);
        Assert.Null(consumo.CustoEstimado);
        Assert.Null(consumo.Moeda);
    }

    [Fact]
    public void Tokens_zero_informados_pelo_provedor_sao_preservados()
    {
        var consumo = RegistradorConsumo.Montar(Registro(0, 0), Preco);

        Assert.Equal(OrigemTokens.Informado, consumo.OrigemTokens);
        Assert.Equal(0, consumo.TokensEntrada);
        Assert.Equal(0m, consumo.CustoEstimado);
    }

    [Fact]
    public void Retentativa_gera_registro_proprio()
    {
        var original = RegistradorConsumo.Montar(Registro(10, 10), Preco);
        var retentativa = RegistradorConsumo.Montar(Registro(10, 10, tentativa: 2), Preco);

        Assert.NotEqual(original.Id, retentativa.Id);
        Assert.Equal(2, retentativa.Tentativa);
    }

    [Fact]
    public void Arquivo_de_precos_versionado_e_lido()
    {
        var arquivo = CarregadorTabelaPrecos.LerArquivo();

        Assert.False(string.IsNullOrWhiteSpace(arquivo.Versao));
        Assert.NotNull(arquivo.Modelos);
    }
}
