using Api.Agente.Application.Execucoes;
using Api.Agente.Application.TabelasReferencia;
using Api.Agente.Application.Validacao;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Tests.Orquestracao;

/// <summary>Regras de status da P-04 e descarte do arquivo (FR-082), sem bancos.</summary>
public class StatusExecucaoTests
{
    private static readonly DadosEfd Dados = DadosEfd.Carregar();

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private sealed class RepositorioMemoria : IRepositorioExecucoes
    {
        public Dictionary<Guid, Execucao> Itens { get; } = [];

        public Task SalvarAsync(Execucao execucao, CancellationToken cancelamento)
        {
            Itens[execucao.Id] = execucao;
            return Task.CompletedTask;
        }

        public Task<Execucao?> ObterAsync(string orgId, Guid id, CancellationToken cancelamento) =>
            Task.FromResult(Itens.TryGetValue(id, out var e) && e.OrgId == orgId ? e : null);

        public Task<IReadOnlyList<Execucao>> ListarAsync(string orgId, int pagina, int tamanho, CancellationToken cancelamento) =>
            Task.FromResult<IReadOnlyList<Execucao>>([.. Itens.Values.Where(e => e.OrgId == orgId)]);

        public Task<bool> ExcluirAsync(string orgId, Guid id, CancellationToken cancelamento) =>
            Task.FromResult(Itens.Remove(id));

        public Task<IReadOnlyList<Execucao>> ListarEmAndamentoAsync(CancellationToken cancelamento) =>
            Task.FromResult<IReadOnlyList<Execucao>>([.. Itens.Values.Where(e => !e.EstaFinalizada)]);
    }

    private sealed class ArmazenamentoMemoria : IArmazenamentoTemporario
    {
        public Dictionary<string, byte[]> Arquivos { get; } = [];

        public bool FalharNaLeitura { get; set; }

        public Exception? ErroAoSalvar { get; set; }

        public async Task<ArquivoRecebido> SalvarAsync(Stream conteudo, string nome, CancellationToken cancelamento)
        {
            if (ErroAoSalvar is not null)
            {
                throw ErroAoSalvar;
            }

            using var memoria = new MemoryStream();
            await conteudo.CopyToAsync(memoria, cancelamento);
            var caminho = Guid.NewGuid().ToString("N");
            Arquivos[caminho] = memoria.ToArray();
            return new ArquivoRecebido(caminho, nome, memoria.Length, new string('a', 64));
        }

        public Stream AbrirLeitura(string caminho) =>
            FalharNaLeitura ? throw new IOException("linha |0150|segredo|") : new MemoryStream(Arquivos[caminho]);

        public void Excluir(string caminho) => Arquivos.Remove(caminho);
    }

    private sealed class FilaMemoria : IFilaExecucoes
    {
        public Queue<ItemFila> Itens { get; } = new();

        public ValueTask EnfileirarAsync(ItemFila item, CancellationToken cancelamento)
        {
            Itens.Enqueue(item);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LimitadorMemoria(int maximo = 10) : ILimitadorExecucoes
    {
        public int EmUso { get; private set; }

        public bool TentarReservar(string orgId)
        {
            if (EmUso >= maximo)
            {
                return false;
            }

            EmUso++;
            return true;
        }

        public void Liberar(string orgId) => EmUso--;
    }

    private sealed class TabelasNcmMemoria : IRepositorioNcm
    {
        public ITabelaNcm? Vigente { get; set; }

        public Task<bool> ImportarAsync(TabelaNcmImportada tabela, IReadOnlyList<LinhaNcm> linhas, CancellationToken cancelamento) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<TabelaNcmImportada>> ListarAsync(CancellationToken cancelamento) =>
            Task.FromResult<IReadOnlyList<TabelaNcmImportada>>([]);

        public Task<ITabelaNcm?> CarregarVigenteAsync(DateOnly periodoInicio, DateOnly periodoFim, CancellationToken cancelamento) =>
            Task.FromResult(Vigente);
    }

    private sealed class Cenario
    {
        public RepositorioMemoria Repositorio { get; } = new();
        public ArmazenamentoMemoria Armazenamento { get; } = new();
        public FilaMemoria Fila { get; } = new();
        public LimitadorMemoria Limitador { get; } = new();
        public TabelasNcmMemoria TabelasNcm { get; } = new();

        public ValidarArquivoHandler Handler => new(
            Repositorio, Armazenamento, Fila, Limitador, Dados, TabelasNcm, new ParametrosValidacao(1_000), TimeProvider.System);

        public async Task<Execucao> ProcessarAsync(string caso)
        {
            await using var arquivo = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", $"{caso}.txt"));
            var recebida = await Handler.ReceberAsync(new ComandoValidacao(arquivo, $"{caso}.txt", "org", "usuario", "correlacao", CanalExecucao.Rest), Cancelamento);
            await Handler.ProcessarAsync(Fila.Itens.Dequeue(), Cancelamento);
            return Repositorio.Itens[recebida.Id];
        }
    }

    [Fact]
    public async Task Arquivo_valido_fica_com_limitacoes_enquanto_a_analise_nao_executa()
    {
        var execucao = await new Cenario().ProcessarAsync("valido-perfil-a");

        Assert.Equal(EstadoExecucao.ConcluidaComLimitacoes, execucao.Estado);
        Assert.Empty(execucao.Achados);
        Assert.Equal("020", execucao.Identificacao!.CodVer);
        Assert.Equal(StatusEtapa.Concluida, execucao.Etapas[3].Status);
        Assert.All(execucao.Etapas.Skip(4).Take(4), e => Assert.Equal(StatusEtapa.NaoExecutada, e.Status));
        Assert.Equal(StatusEtapa.Concluida, execucao.Etapas[8].Status);
        Assert.Contains(execucao.ValidacoesNaoRealizadas, v => v.Referencia == "etapa-5");
        Assert.True(execucao.RevisaoHumanaNecessaria);
    }

    [Theory]
    [InlineData("valido-perfil-a")]
    [InlineData("sem-k990")]
    [InlineData("truncado-sem-9999")]
    public async Task Nenhuma_execucao_fica_concluida_com_validacao_nao_realizada(string caso)
    {
        var execucao = await new Cenario().ProcessarAsync(caso);

        Assert.NotEmpty(execucao.ValidacoesNaoRealizadas);
        Assert.NotEqual(EstadoExecucao.Concluida, execucao.Estado);
    }

    [Theory]
    [InlineData("periodo-2025")]
    [InlineData("periodo-2027")]
    public async Task Periodo_fora_do_leiaute_020_falha_na_etapa_3_sem_executar_as_seguintes(string caso)
    {
        var execucao = await new Cenario().ProcessarAsync(caso);

        Assert.Equal(EstadoExecucao.Falha, execucao.Estado);
        Assert.Equal(StatusEtapa.Falha, execucao.Etapas[2].Status);
        Assert.Contains("Período não suportado", execucao.MotivoFalha, StringComparison.Ordinal);
        Assert.Contains("leiaute 020", execucao.MotivoFalha, StringComparison.Ordinal);
        Assert.All(execucao.Etapas.Skip(3), e => Assert.Equal(StatusEtapa.NaoExecutada, e.Status));
        Assert.Empty(execucao.RegrasExecutadas);
    }

    [Theory]
    [InlineData("valido-perfil-a")]
    [InlineData("periodo-2025")]
    public async Task Arquivo_e_apagado_e_vaga_liberada_ao_fim(string caso)
    {
        var cenario = new Cenario();

        await cenario.ProcessarAsync(caso);

        Assert.Empty(cenario.Armazenamento.Arquivos);
        Assert.Equal(0, cenario.Limitador.EmUso);
    }

    [Fact]
    public async Task Falha_tecnica_apaga_o_arquivo_e_nao_expoe_o_conteudo()
    {
        var cenario = new Cenario();
        cenario.Armazenamento.FalharNaLeitura = true;

        var execucao = await cenario.ProcessarAsync("valido-perfil-a");

        Assert.Equal(EstadoExecucao.Falha, execucao.Estado);
        Assert.DoesNotContain("segredo", execucao.MotivoFalha, StringComparison.Ordinal);
        Assert.Equal(StatusEtapa.Falha, execucao.Etapas[2].Status);
        Assert.Empty(cenario.Armazenamento.Arquivos);
        Assert.Equal(0, cenario.Limitador.EmUso);
    }

    [Fact]
    public async Task Limite_de_execucoes_recusa_sem_gravar_o_arquivo()
    {
        var cenario = new Cenario();
        var limitado = new ValidarArquivoHandler(
            cenario.Repositorio, cenario.Armazenamento, cenario.Fila, new LimitadorMemoria(0), Dados, cenario.TabelasNcm, new ParametrosValidacao(1), TimeProvider.System);

        await Assert.ThrowsAsync<LimiteExecucoesException>(() => limitado.ReceberAsync(
            new ComandoValidacao(new MemoryStream([1]), "a.txt", "org", "u", "c", CanalExecucao.Rest), Cancelamento));

        Assert.Empty(cenario.Armazenamento.Arquivos);
    }

    [Fact]
    public async Task Arquivo_recusado_libera_a_vaga_e_nao_cria_execucao()
    {
        var cenario = new Cenario();
        cenario.Armazenamento.ErroAoSalvar = new ArquivoRecusadoException(MotivoRecusaArquivo.FormatoInvalido, "recusado");

        await Assert.ThrowsAsync<ArquivoRecusadoException>(() => cenario.Handler.ReceberAsync(
            new ComandoValidacao(new MemoryStream([1]), "a.txt", "org", "u", "c", CanalExecucao.Rest), Cancelamento));

        Assert.Equal(0, cenario.Limitador.EmUso);
        Assert.Empty(cenario.Repositorio.Itens);
    }

    [Fact]
    public async Task Sem_tabela_de_ncm_a_execucao_conclui_e_lista_a_regra_como_falha_tecnica()
    {
        var execucao = await new Cenario().ProcessarAsync("ncm-inexistente");

        Assert.Equal(EstadoExecucao.ConcluidaComLimitacoes, execucao.Estado);
        Assert.Equal(Categoria.FalhaTecnica, Assert.Single(execucao.ValidacoesNaoRealizadas, v => v.Referencia == RegrasNegocio.NcmVigente).Categoria);
        Assert.Empty(execucao.Achados);
    }

    [Fact]
    public async Task Limites_permanentes_ficam_fora_do_alcance_e_nao_entre_as_nao_realizadas()
    {
        var cenario = new Cenario();
        cenario.TabelasNcm.Vigente = new TabelaNcm("2026.1", [], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var execucao = await cenario.ProcessarAsync("cbs-ibs-2026");

        Assert.Equal(
            [RegrasNegocio.LacunaNumeracao, RegrasNegocio.TributosReforma],
            execucao.ForaDoAlcance.Select(f => f.Referencia).Order());
        // Com a tabela de NCM, sobram só as etapas de base e IA, que chegam na US3.
        Assert.All(execucao.ValidacoesNaoRealizadas, v => Assert.StartsWith("etapa-", v.Referencia, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Com_tabela_de_ncm_o_achado_entra_no_resultado()
    {
        var cenario = new Cenario();
        cenario.TabelasNcm.Vigente = new TabelaNcm("2026.1", [new CodigoNcm("32041700", null, null)], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var execucao = await cenario.ProcessarAsync("ncm-inexistente");

        var achado = Assert.Single(execucao.Achados);
        Assert.Equal(RegrasNegocio.NcmVigente, achado.RegraId);
        Assert.Equal(Categoria.InconsistenciaNegocio, achado.Categoria);
        Assert.Contains(RegrasNegocio.NcmVigente, execucao.RegrasExecutadas);
    }

    [Fact]
    public async Task Reinicio_do_servico_marca_as_execucoes_em_andamento_como_falha()
    {
        var cenario = new Cenario();
        await using var arquivo = new MemoryStream([1]);
        var recebida = await cenario.Handler.ReceberAsync(new ComandoValidacao(arquivo, "a.txt", "org", "u", "c", CanalExecucao.Rest), Cancelamento);

        await cenario.Handler.FalharEmAndamentoAsync(Cancelamento);

        var execucao = cenario.Repositorio.Itens[recebida.Id];
        Assert.Equal(EstadoExecucao.Falha, execucao.Estado);
        Assert.Contains("reiniciou", execucao.MotivoFalha, StringComparison.Ordinal);
        Assert.True(execucao.RevisaoHumanaNecessaria);
    }
}
