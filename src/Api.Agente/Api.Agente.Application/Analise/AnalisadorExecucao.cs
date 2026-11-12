using System.Globalization;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Analise;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Application.Analise;

/// <summary>Etapas 5 a 8 da P-05: consulta à base, ferramentas, análise do LLM e validação da saída.</summary>
public sealed class AnalisadorExecucao(IBaseInformacao baseInformacao, IModeloAnalise modelo, ParametrosAnalise parametros)
{
    public const string SemTrechos = "Nenhum trecho da base de informação foi encontrado para os achados desta execução.";

    private const int TamanhoMaximoConsulta = 1_000;

    public string Modelo => modelo.Modelo;

    /// <summary>Etapa 5. Lança <see cref="BaseIndisponivelException"/> quando a base não responde.</summary>
    /// <returns>Limitações da consulta, como a ausência de trechos (FR-052).</returns>
    public async Task<IReadOnlyList<string>> ConsultarBaseAsync(Execucao execucao, ContextoRecuperacao recuperacao, CancellationToken cancelamento)
    {
        var periodo = Periodo(execucao);
        var algumTrecho = false;
        foreach (var consulta in Consultas(execucao).Take(parametros.ConsultasPorExecucao))
        {
            var resultado = await baseInformacao.BuscarAsync(
                new ConsultaBase(consulta, execucao.CorrelacaoId, Periodo: periodo, TopK: parametros.TopK), recuperacao, cancelamento);
            algumTrecho |= !resultado.SemResultados && resultado.Trechos.Count > 0;
        }

        return algumTrecho ? [] : [SemTrechos];
    }

    /// <summary>
    /// Etapas 6 a 8. Resposta inválida gera nova tentativa com os erros, até o limite configurado; esgotado,
    /// a análise fica com status falha e o relatório determinístico segue sem ela (FR-084). Lança
    /// <see cref="ModeloIndisponivelException"/> quando o provedor não responde.
    /// </summary>
    public async Task<AnaliseIa> AnalisarAsync(
        Execucao execucao, ContextoRecuperacao recuperacao, IReadOnlyCollection<string> limitacoesBase, CancellationToken cancelamento)
    {
        var sessao = new SessaoFerramentas(
            TipoSessao.Validacao,
            execucao.Id,
            execucao.OrgId,
            execucao.CorrelacaoId,
            [SessaoFerramentas.PapelSistema],
            recuperacao,
            [.. execucao.Achados],
            Periodo(execucao),
            temExecucao: true);

        var deterministicos = execucao.Achados.Where(a => a.Origem == Origem.Validador).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var prompt = ConstrutorPrompt.ParaAnalise(execucao, recuperacao.Trechos, limitacoesBase);
        var mensagens = prompt.Mensagens.ToList();
        var tentativas = parametros.RetentativasResposta + 1;

        for (var tentativa = 1; tentativa <= tentativas; tentativa++)
        {
            var resposta = await modelo.GerarAsync(new PedidoModelo(prompt.InstrucoesSistema, mensagens, FormatoResposta.Analise, sessao, tentativa), cancelamento);
            var validacao = ValidadorRespostaLlm.ValidarAnalise(resposta.Conteudo, deterministicos, recuperacao);
            if (validacao.Valida)
            {
                var analise = ValidadorRespostaLlm.MontarAnaliseAceita(validacao.Conteudo!, recuperacao, modelo.Modelo, tentativa);
                return limitacoesBase.Count == 0
                    ? analise
                    : analise with { Conteudo = analise.Conteudo! with { Limitacoes = [.. limitacoesBase.Union(analise.Conteudo.Limitacoes, StringComparer.Ordinal)] } };
            }

            mensagens.Add(new MensagemModelo(false, resposta.Conteudo));
            mensagens.Add(ConstrutorPrompt.Correcao(validacao.Erros));
        }

        return new AnaliseIa(StatusAnaliseIa.Falha, modelo.Modelo, tentativas, null, [], false);
    }

    public static string Periodo(Execucao execucao) =>
        execucao.Identificacao?.PeriodoInicio.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("A análise exige a identificação do arquivo.");

    /// <summary>Uma consulta por regra com achados, priorizando as mais graves; sem achados, uma consulta geral.</summary>
    private static IEnumerable<string> Consultas(Execucao execucao)
    {
        if (execucao.Achados.Count == 0)
        {
            yield return $"Orientações gerais de preenchimento da EFD ICMS/IPI para o perfil {execucao.Identificacao?.IndPerfil}: blocos e registros obrigatórios.";
            yield break;
        }

        var grupos = execucao.Achados
            .GroupBy(a => a.RegraId ?? a.Registro ?? a.Categoria.ToString())
            .OrderBy(g => g.Min(a => (int)a.Severidade))
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        foreach (var grupo in grupos)
        {
            var achado = grupo.First();
            var consulta = string.Join(' ', new[] { achado.Registro is null ? null : $"Registro {achado.Registro}", achado.Campo is null ? null : $"campo {achado.Campo}", achado.Mensagem }
                .Where(p => p is not null));
            yield return consulta.Length <= TamanhoMaximoConsulta ? consulta : consulta[..TamanhoMaximoConsulta];
        }
    }
}
