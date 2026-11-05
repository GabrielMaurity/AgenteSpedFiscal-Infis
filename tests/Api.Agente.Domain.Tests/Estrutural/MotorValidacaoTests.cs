using System.Text;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Catalogo;
using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Validacao;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Domain.Tests.Estrutural;

public class MotorValidacaoTests
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static string Descrever(IEnumerable<Achado> achados) =>
        string.Join("\n", achados.Select(a => $"{a.RegraId} L{a.Linha} {a.Registro}/{a.Campo}: {a.Mensagem}"));

    private static MemoryStream Arquivo(string conteudo) => new(Encoding.Latin1.GetBytes(conteudo));

    private static async Task<string> TextoValidoAsync() =>
        Encoding.Latin1.GetString(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", "valido-perfil-a.txt"), Cancelamento));

    [Theory]
    [InlineData("valido-perfil-a")]
    [InlineData("sem-k990")]
    [InlineData("c170-orfao")]
    [InlineData("ind-mov-incoerente")]
    [InlineData("9900-divergente")]
    [InlineData("campos-faltando")]
    [InlineData("caractere-invalido")]
    [InlineData("injecao-prompt")]
    public async Task Achados_batem_exatamente_com_o_gabarito(string caso)
    {
        var gabarito = FixturesEfd.LerGabarito(caso);

        var resultado = await FixturesEfd.ValidarCasoAsync(caso, Cancelamento);

        Assert.True(
            gabarito.Achados!.Count == resultado.Achados.Count
            && gabarito.Achados.All(e => resultado.Achados.Any(a => FixturesEfd.Corresponde(a, e))),
            $"Caso {caso}. Encontrados:\n{Descrever(resultado.Achados)}");
    }

    [Fact]
    public async Task Arquivo_truncado_aponta_o_fim_ausente_e_os_blocos_faltantes()
    {
        var gabarito = FixturesEfd.LerGabarito("truncado-sem-9999");

        var resultado = await FixturesEfd.ValidarCasoAsync("truncado-sem-9999", Cancelamento);

        Assert.All(gabarito.AchadosContem!, e => Assert.Contains(resultado.Achados, a => FixturesEfd.Corresponde(a, e)));
    }

    [Theory]
    [InlineData("sem-k990")]
    [InlineData("c170-orfao")]
    [InlineData("ind-mov-incoerente")]
    [InlineData("9900-divergente")]
    public async Task Achado_estrutural_traz_regra_fonte_oficial_e_severidade(string caso)
    {
        var resultado = await FixturesEfd.ValidarCasoAsync(caso, Cancelamento);

        Assert.All(resultado.Achados, a =>
        {
            Assert.Equal(Categoria.ErroEstrutural, a.Categoria);
            Assert.Equal(Severidade.Impeditiva, a.Severidade);
            Assert.Equal(Origem.Validador, a.Origem);
            Assert.False(string.IsNullOrWhiteSpace(a.Fonte!.Localizacao));
            Assert.False(string.IsNullOrWhiteSpace(a.AcaoCorretiva));
        });
    }

    [Fact]
    public async Task Resultado_lista_as_regras_executadas()
    {
        var resultado = await FixturesEfd.ValidarCasoAsync("valido-perfil-a", Cancelamento);

        Assert.Contains(RegrasEstruturais.AberturaEncerramento, resultado.RegrasExecutadas);
        Assert.Equal(18, resultado.RegrasExecutadas.Count);
        Assert.Empty(resultado.NaoRealizadas);
    }

    [Fact]
    public async Task Texto_malicioso_nao_muda_os_achados()
    {
        var limpo = await FixturesEfd.ValidarCasoAsync("valido-perfil-a", Cancelamento);
        var malicioso = await FixturesEfd.ValidarCasoAsync("injecao-prompt", Cancelamento);

        Assert.Equal(limpo.Achados.Count, malicioso.Achados.Count);
        Assert.Equal(limpo.RegrasExecutadas, malicioso.RegrasExecutadas);
    }

    [Fact]
    public async Task Linha_sem_delimitador_e_apontada()
    {
        var texto = await TextoValidoAsync() ;
        var resultado = await FixturesEfd.ValidarAsync(Arquivo(texto.Replace("|0190|UN|UNIDADE|", "0190|UN|UNIDADE", StringComparison.Ordinal)), Cancelamento);

        Assert.Contains(resultado.Achados, a => a.RegraId == RegrasEstruturais.Delimitador);
    }

    [Fact]
    public async Task Linha_sem_cr_lf_e_apontada()
    {
        var texto = await TextoValidoAsync();
        var resultado = await FixturesEfd.ValidarAsync(Arquivo(texto.Replace("\r\n", "\n", StringComparison.Ordinal)), Cancelamento);

        Assert.Contains(resultado.Achados, a => a.RegraId == RegrasEstruturais.FimDeLinha && a.Linha == 1);
    }

    [Fact]
    public async Task Registro_inexistente_no_leiaute_e_apontado()
    {
        var texto = await TextoValidoAsync();
        var resultado = await FixturesEfd.ValidarAsync(Arquivo(texto.Replace("|0190|UN|UNIDADE|", "|ZZZZ|UN|UNIDADE|", StringComparison.Ordinal)), Cancelamento);

        Assert.Contains(resultado.Achados, a => a.RegraId == RegrasEstruturais.RegistroDesconhecido && a.Registro == "ZZZZ");
    }

    [Fact]
    public async Task Campo_numerico_invalido_e_apontado()
    {
        var texto = await TextoValidoAsync();
        var resultado = await FixturesEfd.ValidarAsync(Arquivo(texto.Replace("|100,00|", "|1.000,00|", StringComparison.Ordinal)), Cancelamento);

        Assert.Contains(resultado.Achados, a => a.RegraId == RegrasEstruturais.CampoNumerico && a.ValorEncontrado == "1.000,00");
    }

    [Fact]
    public async Task Excesso_de_casas_decimais_e_apontado()
    {
        var texto = await TextoValidoAsync();
        var resultado = await FixturesEfd.ValidarAsync(Arquivo(texto.Replace("|18,00|", "|18,123|", StringComparison.Ordinal)), Cancelamento);

        Assert.Contains(resultado.Achados, a => a.RegraId == RegrasEstruturais.CasasDecimais);
    }

    [Fact]
    public async Task Cnpj_alfanumerico_do_leiaute_020_nao_gera_achado()
    {
        var texto = await TextoValidoAsync();
        Assert.Contains("12ABC34501DE35", texto, StringComparison.Ordinal);

        var resultado = await FixturesEfd.ValidarAsync(Arquivo(texto), Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.Campo == "CNPJ");
    }

    [Fact]
    public async Task Total_conta_alem_do_limite_de_ocorrencias_detalhadas()
    {
        var texto = await TextoValidoAsync();
        var resultado = await FixturesEfd.ValidarAsync(Arquivo(texto.Replace("\r\n", "\n", StringComparison.Ordinal)), Cancelamento, limite: 2);

        Assert.Equal(2, resultado.Achados.Count(a => a.RegraId == RegrasEstruturais.FimDeLinha));
        Assert.Equal(resultado.TotalLinhas, resultado.Totais[RegrasEstruturais.FimDeLinha]);
    }

    [Fact]
    public async Task Regra_pendente_nao_executa_e_fica_como_nao_realizada()
    {
        var dados = FixturesEfd.Dados;
        var pendente = dados.Catalogo.Regras.First(r => r.Id == RegrasEstruturais.AberturaEncerramento) with { Status = "pendente_confirmacao", Fonte = null };
        var catalogo = dados.Catalogo with { Regras = [.. dados.Catalogo.Regras.Where(r => r.Id != pendente.Id), pendente] };
        await using var arquivo = FixturesEfd.Abrir("sem-k990");
        var identificacao = (await FixturesEfd.IdentificarAsync(arquivo, Cancelamento)).Identificacao!;

        var resultado = await MotorValidacao.ValidarAsync(arquivo, dados.Leiautes[0], catalogo, identificacao, 1_000, Cancelamento);

        Assert.DoesNotContain(resultado.Achados, a => a.RegraId == pendente.Id);
        Assert.DoesNotContain(pendente.Id, resultado.RegrasExecutadas);
        Assert.Contains(resultado.NaoRealizadas, n => n.Referencia == pendente.Id && n.Categoria == Categoria.AusenciaEvidencias);
    }

    [Fact]
    public async Task Arquivo_vazio_e_apontado()
    {
        var dados = FixturesEfd.Dados;
        var identificacao = new IdentificacaoArquivo("020", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), "0", "SP", "x", "A");

        var resultado = await MotorValidacao.ValidarAsync(Arquivo(""), dados.Leiautes[0], dados.Catalogo, identificacao, 1_000, Cancelamento);

        Assert.Contains(resultado.Achados, a => a.RegraId == RegrasEstruturais.InicioEFim);
    }

    [Fact]
    public void Catalogo_recusa_regra_ativa_sem_fonte_ou_com_fonte_nao_oficial()
    {
        var catalogo = FixturesEfd.Dados.Catalogo;
        var regra = catalogo.Regras[0];

        var semFonte = catalogo with { Regras = [regra with { Fonte = null }] };
        var interna = catalogo with { Regras = [regra with { Fonte = new Fonte("Procedimento interno", "1", "p. 1") }] };

        Assert.Throws<InvalidOperationException>(semFonte.GarantirIntegridade);
        Assert.Throws<InvalidOperationException>(interna.GarantirIntegridade);
    }

    [Theory]
    [InlineData("020", 2026, 1, true)]
    [InlineData("020", 2026, 12, true)]
    [InlineData("019", 2025, 12, false)]
    [InlineData("021", 2027, 1, false)]
    [InlineData("020", 2027, 1, false)]
    [InlineData("019", 2026, 6, false)]
    public void Seletor_valida_cod_ver_contra_dt_fin(string codVer, int ano, int mes, bool aceito)
    {
        var fim = new DateOnly(ano, mes, 28);
        var identificacao = new IdentificacaoArquivo(codVer, fim.AddDays(-27), fim, "0", "SP", "x", "A");

        var selecao = SeletorLeiaute.Selecionar(identificacao, FixturesEfd.Dados.Leiautes);

        Assert.Equal(aceito, selecao.Leiaute is not null);
        Assert.Equal(aceito, selecao.MotivoRecusa is null);
    }

    [Fact]
    public void Recusa_informa_a_versao_e_o_periodo_aceitos()
    {
        var fim = new DateOnly(2027, 1, 31);
        var identificacao = new IdentificacaoArquivo("021", new DateOnly(2027, 1, 1), fim, "0", "SP", "x", "A");

        var selecao = SeletorLeiaute.Selecionar(identificacao, FixturesEfd.Dados.Leiautes);

        Assert.Contains("leiaute 020", selecao.MotivoRecusa, StringComparison.Ordinal);
        Assert.Contains("31/12/2026", selecao.MotivoRecusa, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C100", "CHV_NFE", "C")]
    [InlineData("0000", "CNPJ", "C")]
    [InlineData("0150", "CNPJ", "C")]
    [InlineData("0000", "COD_VER", "N")]
    public void Leiaute_020_tem_os_tipos_da_nota_tecnica(string registro, string campo, string tipo)
    {
        var definicao = FixturesEfd.Dados.Leiautes[0].ObterRegistro(registro)!;

        Assert.Equal(tipo, definicao.Campos.Single(c => c.Nome == campo).Tipo);
    }

    [Theory]
    [InlineData("C197", "IND_BENEFICIO")]
    [InlineData("E111", "IND_BENEFICIO")]
    [InlineData("C180", "COD_PROD")]
    public void Leiaute_020_nao_tem_campos_de_2027(string registro, string campo)
    {
        var definicao = FixturesEfd.Dados.Leiautes[0].ObterRegistro(registro)!;

        Assert.DoesNotContain(definicao.Campos, c => c.Nome == campo);
    }

    [Theory]
    [InlineData("C170", "C100", 3)]
    [InlineData("C190", "C100", 3)]
    [InlineData("E110", "E100", 3)]
    [InlineData("H010", "H005", 3)]
    public void Leiaute_020_tem_a_hierarquia_do_guia(string registro, string pai, int nivel)
    {
        var definicao = FixturesEfd.Dados.Leiautes[0].ObterRegistro(registro)!;

        Assert.Equal(pai, definicao.Pai);
        Assert.Equal(nivel, definicao.Nivel);
    }

    [Fact]
    public void Identificacao_recusa_arquivo_sem_registro_0000()
    {
        var resultado = Domain.Efd.Identificacao.IdentificadorArquivo.Identificar(new Domain.Efd.Leitura.LinhaEfd(1, "|C100|x|", true));

        Assert.Null(resultado.Identificacao);
        Assert.NotNull(resultado.MotivoRecusa);
    }
}
