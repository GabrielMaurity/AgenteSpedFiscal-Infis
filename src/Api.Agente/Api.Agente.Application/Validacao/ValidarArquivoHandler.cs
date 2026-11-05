using Api.Agente.Application.Execucoes;
using Api.Agente.Domain.Achados;
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
    ParametrosValidacao parametros,
    TimeProvider relogio)
{
    /// <summary>Etapas que dependem da base de informação e do LLM, entregues na história 3.</summary>
    private static readonly int[] EtapasDeAnalise = [5, 6, 7, 8];

    private const string MotivoAnaliseIndisponivel = "Consulta à base de informação e análise da IA ainda não disponíveis nesta versão.";
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

    /// <summary>Execuções em andamento quando o serviço reiniciou viram falha, pois o arquivo se perdeu (research R-04).</summary>
    public async Task FalharEmAndamentoAsync(CancellationToken cancelamento)
    {
        foreach (var execucao in await repositorio.ListarEmAndamentoAsync(cancelamento))
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

        ResultadoValidacao resultado;
        await using (var arquivo = armazenamento.AbrirLeitura(caminho))
        {
            resultado = await MotorValidacao.ValidarAsync(
                arquivo, leiaute!, dados.Catalogo, identificacao!, parametros.OcorrenciasDetalhadasPorRegra, cancelamento);
        }

        execucao.AplicarResultado(resultado.Achados, resultado.Totais, resultado.RegrasExecutadas, resultado.NaoRealizadas);
        execucao.AtualizarEtapa(4, StatusEtapa.Concluida, relogio.GetUtcNow());

        foreach (var etapa in EtapasDeAnalise)
        {
            execucao.AtualizarEtapa(etapa, StatusEtapa.NaoExecutada, relogio.GetUtcNow(), MotivoAnaliseIndisponivel);
            execucao.RegistrarValidacaoNaoRealizada(new ValidacaoNaoRealizada($"etapa-{etapa}", Categoria.AusenciaEvidencias, MotivoAnaliseIndisponivel));
        }

        execucao.AtualizarEtapa(9, StatusEtapa.Concluida, relogio.GetUtcNow());
        execucao.Concluir(analiseIaAceita: false);
        await repositorio.SalvarAsync(execucao, cancelamento);
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
