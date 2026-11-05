using System.Text;
using Api.Agente.Application.TabelasReferencia;
using Api.Agente.Domain.Efd.Validacao.Negocio;

namespace Api.Agente.Application.Tests.TabelasReferencia;

public class ImportarNcmTests
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private sealed class RepositorioFalso(bool aceitar = true) : IRepositorioNcm
    {
        public TabelaNcmImportada? Gravada { get; private set; }

        public Task<bool> ImportarAsync(TabelaNcmImportada tabela, IReadOnlyList<LinhaNcm> linhas, CancellationToken cancelamento)
        {
            Gravada = tabela;
            return Task.FromResult(aceitar);
        }

        public Task<IReadOnlyList<TabelaNcmImportada>> ListarAsync(CancellationToken cancelamento) =>
            Task.FromResult<IReadOnlyList<TabelaNcmImportada>>([]);

        public Task<ITabelaNcm?> CarregarVigenteAsync(DateOnly periodoInicio, DateOnly periodoFim, CancellationToken cancelamento) =>
            Task.FromResult<ITabelaNcm?>(null);
    }

    private static byte[] Utf8(string texto) => Encoding.UTF8.GetBytes(texto);

    private static ComandoImportarNcm Comando(byte[] conteudo, DateOnly? fim = null) =>
        new(conteudo, "Portal Único", "2026.1", new DateOnly(2026, 1, 1), fim, "admin");

    [Fact]
    public void Le_json_com_nomenclaturas_e_ignora_posicoes_sem_8_digitos()
    {
        var linhas = ParserTabelaNcm.Ler(Utf8("""
            { "Data_Ultima_Atualizacao_NCM": "01/01/2026", "Nomenclaturas": [
              { "Codigo": "01", "Descricao": "Capítulo", "Data_Inicio": "01/04/2022", "Data_Fim": "31/12/9999" },
              { "Codigo": "0101.21.00", "Descricao": "Reprodutores", "Data_Inicio": "01/04/2022", "Data_Fim": "31/12/9999" },
              { "Codigo": "0101.29.00", "Descricao": "Outros", "Data_Inicio": "01/04/2022", "Data_Fim": "31/12/2025" }
            ] }
            """));

        Assert.Equal(2, linhas.Count);
        Assert.Equal(new LinhaNcm("01012100", "Reprodutores", new DateOnly(2022, 4, 1), null), linhas[0]);
        Assert.Equal(new DateOnly(2025, 12, 31), linhas[1].VigenciaFim);
    }

    [Theory]
    [InlineData("codigo;descricao\n32041700;Pigmentos\r\n", null)]
    [InlineData("|32041700|Pigmentos|\n", null)]
    [InlineData("32041700\tPigmentos\t01/01/2026\t\n", "2026-01-01")]
    [InlineData("3204.17.00;Pigmentos;2026-01-01;\n", "2026-01-01")]
    public void Le_texto_delimitado_e_ignora_o_cabecalho(string conteudo, string? inicio)
    {
        var linha = Assert.Single(ParserTabelaNcm.Ler(Utf8(conteudo)));

        Assert.Equal("32041700", linha.Codigo);
        Assert.Equal("Pigmentos", linha.Descricao);
        Assert.Equal(inicio is null ? null : DateOnly.Parse(inicio, System.Globalization.CultureInfo.InvariantCulture), linha.VigenciaInicio);
    }

    [Fact]
    public void Le_arquivo_em_latin1()
    {
        var linha = Assert.Single(ParserTabelaNcm.Ler(Encoding.Latin1.GetBytes("32041700;Corantes orgânicos\n")));

        Assert.Equal("Corantes orgânicos", linha.Descricao);
    }

    [Theory]
    [InlineData("texto sem nenhum código")]
    [InlineData("{ \"Nomenclaturas\": [] }")]
    [InlineData("{ \"outra\": 1 }")]
    [InlineData("{ quebrado")]
    [InlineData("32041700;Pigmentos;data-ruim;\n")]
    public void Conteudo_sem_codigo_valido_e_recusado(string conteudo)
    {
        Assert.Throws<TabelaNcmInvalidaException>(() => ParserTabelaNcm.Ler(Utf8(conteudo)));
    }

    [Fact]
    public async Task Importacao_grava_versao_vigencia_hash_e_quantidade()
    {
        var repositorio = new RepositorioFalso();
        var handler = new ImportarNcmHandler(repositorio, TimeProvider.System);
        var conteudo = Utf8("32041700;Pigmentos\n");

        var resultado = await handler.ImportarAsync(Comando(conteudo), Cancelamento);

        Assert.False(resultado.VersaoJaExistia);
        Assert.Equal("2026.1", repositorio.Gravada!.Versao);
        Assert.Equal(1, repositorio.Gravada.QuantidadeCodigos);
        Assert.Equal(64, repositorio.Gravada.Sha256.Length);
    }

    [Fact]
    public async Task Versao_repetida_e_informada_sem_duplicar()
    {
        var handler = new ImportarNcmHandler(new RepositorioFalso(aceitar: false), TimeProvider.System);

        var resultado = await handler.ImportarAsync(Comando(Utf8("32041700;Pigmentos\n")), Cancelamento);

        Assert.True(resultado.VersaoJaExistia);
    }

    [Fact]
    public async Task Vigencia_invertida_e_recusada()
    {
        var handler = new ImportarNcmHandler(new RepositorioFalso(), TimeProvider.System);

        await Assert.ThrowsAsync<TabelaNcmInvalidaException>(() =>
            handler.ImportarAsync(Comando(Utf8("32041700;Pigmentos\n"), new DateOnly(2025, 1, 1)), Cancelamento));
    }
}
