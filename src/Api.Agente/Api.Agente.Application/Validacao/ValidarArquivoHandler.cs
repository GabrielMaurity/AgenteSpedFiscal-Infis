using Api.Agente.Application.Analise;
using Api.Agente.Application.Execucoes;
using Api.Agente.Application.TabelasReferencia;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Analise;
using Api.Agente.Domain.Efd;
using Api.Agente.Domain.Efd.Identificacao;
using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;
using Api.Agente.Domain.Efd.Validacao;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Validacao;

public sealed record ComandoValidacao(Stream Conteudo, string NomeArquivo, string OrgId, string UsuarioId, string CorrelacaoId, CanalExecucao Canal);

/// <summary>Recepção e processamento nas nove etapas da constitution P-05.</summary>
public sealed class ValidarArquivoHandler(
    IRepositorioExecucoes repositorio,
    IArmazenamentoTemporario armazenamento,
    IFilaExecucoes fila,
    ILimitadorExecucoes limitador,
    DadosEfd dados,
    IRepositorioNcm tabelasNcm,
    ParametrosValidacao parametros,
    AnalisadorExecucao analisador,
    TimeProvider relogio)
{
    public const string MotivoBaseIndisponivel = "Base de informação indisponível: a consulta não foi realizada.";
    public const string MotivoAnaliseSemBase = "Análise da IA não executada: depende da consulta à base de informação, que falhou.";
    public const string MotivoModeloIndisponivel = "Análise da IA não realizada: o provedor do modelo não respondeu.";
    public const string MotivoSaidaInvalida = "A saída da IA não passou na validação após as retentativas; o relatório determinístico é entregue sem a análise.";
    private const string MotivoFalhaTecnica = "Falha técnica ao processar o arquivo. Envie o arquivo novamente.";
    private const string MotivoInterrompido = "Etapa não executada: o processamento foi interrompido antes dela.";

    /// <summary>Etapas 1 e 2: grava o arquivo, cria a execução e a coloca na fila.</summary>
    public async Task<Execucao> ReceberAsync(ComandoValidacao comando, CancellationToken cancelamento)
    {
        if (!limitador.TentarReservar(comando.OrgId))
        {
            throw new LimiteExecucoesException();
        }

        ArquivoRecebido? arquivo = null;
        try
        {
            arquivo = await armazenamento.SalvarAsync(comando.Conteudo, comando.NomeArquivo, cancelamento);

            var agora = relogio.GetUtcNow();
            var execucao = Execucao.Criar(Guid.CreateVersion7(), comando.OrgId, comando.UsuarioId, comando.CorrelacaoId, comando.Canal, agora);
            execucao.DefinirArquivo(new ArquivoExecucao(arquivo.Nome, arquivo.TamanhoBytes, arquivo.Sha256));
            execucao.AtualizarEtapa(1, StatusEtapa.Concluida, agora);

            await repositorio.SalvarAsync(execucao, cancelamento);
            await fila.EnfileirarAsync(new ItemFila(execucao.Id, execucao.OrgId, arquivo.Caminho), cancelamento);
            return execucao;
        }
        catch
        {
            if (arquivo is not null)
            {
                armazenamento.Excluir(arquivo.Caminho);
            }

            limitador.Liberar(comando.OrgId);
            throw;
        }
    }

    /// <summary>Etapas 2 a 9. O arquivo é apagado e a vaga é liberada em qualquer desfecho (FR-082).</summary>
    public async Task ProcessarAsync(ItemFila item, CancellationToken cancelamento)
    {
        Execucao? execucao = null;
        try
        {
            execucao = await repositorio.ObterAsync(item.OrgId, item.ExecucaoId, cancelamento);
            if (execucao is not null)
            {
                await ExecutarEtapasAsync(execucao, item.Caminho, cancelamento);
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException && execucao is { EstaFinalizada: false })
        {
            // O detalhe pode citar conteúdo do arquivo: o resultado leva só o motivo genérico.
            RegistrarFalha(execucao, execucao.Etapas.FirstOrDefault(e => e.Status == StatusEtapa.Executando)?.Numero ?? 9, MotivoFalhaTecnica);
            await repositorio.SalvarAsync(execucao, CancellationToken.None);
        }
        finally
        {
            armazenamento.Excluir(item.Caminho);
            limitador.Liberar(item.OrgId);
        }
    }

    /// <summary>
    /// Execuções interrompidas viram falha, pois o arquivo se perdeu (research R-04). Sem limiar (instância
    /// única), todas as em andamento; com limiar (várias instâncias), só as paradas há mais que ele, para não
    /// encerrar o que outra instância ainda processa (feature 002, FR-P06).
    /// </summary>
    public async Task FalharEmAndamentoAsync(TimeSpan? limiar, CancellationToken cancelamento)
    {
        DateTimeOffset? atualizadasAntesDe = limiar is { } valor ? relogio.GetUtcNow() - valor : null;
        foreach (var execucao in await repositorio.ListarEmAndamentoAsync(atualizadasAntesDe, cancelamento))
        {
            RegistrarFalha(
                execucao,
                execucao.Etapas.FirstOrDefault(e => e.Status == StatusEtapa.Executando)?.Numero ?? 2,
                "O serviço reiniciou durante o processamento. Envie o arquivo novamente.");
            await repositorio.SalvarAsync(execucao, cancelamento);
        }
    }

    private async Task ExecutarEtapasAsync(Execucao execucao, string caminho, CancellationToken cancelamento)
    {
        execucao.AvancarPara(EstadoExecucao.ValidandoArquivo);
        execucao.AtualizarEtapa(2, StatusEtapa.Concluida, relogio.GetUtcNow());

        execucao.AvancarPara(EstadoExecucao.Identificando);
        execucao.AtualizarEtapa(3, StatusEtapa.Executando, relogio.GetUtcNow());
        await repositorio.SalvarAsync(execucao, cancelamento);

        var (identificacao, leiaute, recusa) = await IdentificarAsync(caminho, cancelamento);
        if (recusa is not null)
        {
            if (identificacao is not null)
            {
                execucao.DefinirIdentificacao(identificacao);
            }

            RegistrarFalha(execucao, 3, recusa);
            await repositorio.SalvarAsync(execucao, cancelamento);
            return;
        }

        execucao.DefinirIdentificacao(identificacao!);
        execucao.AtualizarEtapa(3, StatusEtapa.Concluida, relogio.GetUtcNow());

        execucao.AvancarPara(EstadoExecucao.Validando);
        execucao.AtualizarEtapa(4, StatusEtapa.Executando, relogio.GetUtcNow());
        await repositorio.SalvarAsync(execucao, cancelamento);

        // Carregada uma vez por execução. Sem tabela, a regra de NCM fica como não realizada (FR-022).
        var tabelaNcm = await tabelasNcm.CarregarVigenteAsync(identificacao!.PeriodoInicio, identificacao.PeriodoFim, cancelamento);

        ResultadoValidacao resultado;
        await using (var arquivo = armazenamento.AbrirLeitura(caminho))
        {
            resultado = await MotorValidacao.ValidarAsync(
                arquivo, leiaute!, dados.Catalogo, identificacao, parametros.OcorrenciasDetalhadasPorRegra, cancelamento, tabelaNcm);
        }

        execucao.AplicarResultado(resultado.Achados, resultado.Totais, resultado.RegrasExecutadas, resultado.NaoRealizadas, resultado.ForaDoAlcance);
        execucao.AtualizarEtapa(4, StatusEtapa.Concluida, relogio.GetUtcNow());

        var analiseAceita = await AnalisarAsync(execucao, cancelamento);

        execucao.AtualizarEtapa(9, StatusEtapa.Concluida, relogio.GetUtcNow());
        execucao.Concluir(analiseAceita);
        await repositorio.SalvarAsync(execucao, cancelamento);
    }

    /// <summary>
    /// Etapas 5 a 8. Falha da base, do modelo ou da validação da saída nunca derruba a execução: os achados
    /// determinísticos são entregues e a limitação fica em validacoesNaoRealizadas (FR-084, SC-004).
    /// </summary>
    private async Task<bool> AnalisarAsync(Execucao execucao, CancellationToken cancelamento)
    {
        execucao.AvancarPara(EstadoExecucao.ConsultandoBase);
        execucao.AtualizarEtapa(5, StatusEtapa.Executando, relogio.GetUtcNow());
        await repositorio.SalvarAsync(execucao, cancelamento);

        var recuperacao = new ContextoRecuperacao();
        IReadOnlyList<string> limitacoesBase;
        try
        {
            limitacoesBase = await analisador.ConsultarBaseAsync(execucao, recuperacao, cancelamento);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancelamento.IsCancellationRequested)
        {
            var agora = relogio.GetUtcNow();
            execucao.AtualizarEtapa(5, StatusEtapa.Falha, agora, MotivoBaseIndisponivel);
            execucao.RegistrarValidacaoNaoRealizada(new ValidacaoNaoRealizada("etapa-5", Categoria.FalhaTecnica, MotivoBaseIndisponivel));
            foreach (var etapa in new[] { 6, 7, 8 })
            {
                execucao.AtualizarEtapa(etapa, StatusEtapa.NaoExecutada, agora, MotivoAnaliseSemBase);
            }

            execucao.RegistrarValidacaoNaoRealizada(new ValidacaoNaoRealizada("etapa-7", Categoria.FalhaTecnica, MotivoAnaliseSemBase));
            return false;
        }

        execucao.AtualizarEtapa(5, StatusEtapa.Concluida, relogio.GetUtcNow(), limitacoesBase.Count > 0 ? limitacoesBase[0] : null);
        execucao.AvancarPara(EstadoExecucao.AnalisandoIa);
        execucao.AtualizarEtapa(6, StatusEtapa.Executando, relogio.GetUtcNow());
        execucao.AtualizarEtapa(7, StatusEtapa.Executando, relogio.GetUtcNow());
        await repositorio.SalvarAsync(execucao, cancelamento);

        AnaliseIa analise;
        try
        {
            analise = await analisador.AnalisarAsync(execucao, recuperacao, limitacoesBase, cancelamento);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancelamento.IsCancellationRequested)
        {
            var agora = relogio.GetUtcNow();
            execucao.AtualizarEtapa(6, StatusEtapa.Falha, agora, MotivoModeloIndisponivel);
            execucao.AtualizarEtapa(7, StatusEtapa.Falha, agora, MotivoModeloIndisponivel);
            execucao.AtualizarEtapa(8, StatusEtapa.NaoExecutada, agora, MotivoModeloIndisponivel);
            execucao.RegistrarValidacaoNaoRealizada(new ValidacaoNaoRealizada("etapa-7", Categoria.FalhaTecnica, MotivoModeloIndisponivel));
            execucao.RegistrarAnaliseIa(new AnaliseIa(StatusAnaliseIa.Falha, analisador.Modelo, 0, null, [], false));
            return false;
        }

        execucao.AtualizarEtapa(6, StatusEtapa.Concluida, relogio.GetUtcNow());
        execucao.AtualizarEtapa(7, StatusEtapa.Concluida, relogio.GetUtcNow());
        execucao.AvancarPara(EstadoExecucao.ValidandoSaida);

        var aceita = analise.Status == StatusAnaliseIa.Aceita;
        if (aceita)
        {
            execucao.AtualizarEtapa(8, StatusEtapa.Concluida, relogio.GetUtcNow());
        }
        else
        {
            execucao.AtualizarEtapa(8, StatusEtapa.Falha, relogio.GetUtcNow(), MotivoSaidaInvalida);
            execucao.RegistrarValidacaoNaoRealizada(new ValidacaoNaoRealizada("etapa-8", Categoria.FalhaTecnica, MotivoSaidaInvalida));
        }

        execucao.RegistrarAnaliseIa(analise);
        return aceita;
    }

    private async Task<(IdentificacaoArquivo? Identificacao, DefinicaoLeiaute? Leiaute, string? Recusa)> IdentificarAsync(string caminho, CancellationToken cancelamento)
    {
        await using var arquivo = armazenamento.AbrirLeitura(caminho);

        LinhaEfd? primeira = null;
        await foreach (var linha in LeitorEfd.LerAsync(arquivo, cancelamento))
        {
            primeira = linha;
            break;
        }

        var resultado = IdentificadorArquivo.Identificar(primeira);
        if (resultado.Identificacao is null)
        {
            return (null, null, resultado.MotivoRecusa);
        }

        var selecao = SeletorLeiaute.Selecionar(resultado.Identificacao, dados.Leiautes);
        return (resultado.Identificacao, selecao.Leiaute, selecao.MotivoRecusa);
    }

    private void RegistrarFalha(Execucao execucao, int etapaComFalha, string motivo)
    {
        var agora = relogio.GetUtcNow();
        execucao.AtualizarEtapa(etapaComFalha, StatusEtapa.Falha, agora, motivo);
        foreach (var etapa in execucao.Etapas.Where(e => e.Numero > etapaComFalha && e.Status == StatusEtapa.Pendente).ToList())
        {
            execucao.AtualizarEtapa(etapa.Numero, StatusEtapa.NaoExecutada, agora, MotivoInterrompido);
        }

        execucao.Falhar(motivo);
    }
}
