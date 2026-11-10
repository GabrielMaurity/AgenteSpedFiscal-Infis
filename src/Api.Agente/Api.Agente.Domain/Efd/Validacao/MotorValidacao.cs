using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd.Catalogo;
using Api.Agente.Domain.Efd.Leiaute;
using Api.Agente.Domain.Efd.Leitura;
using Api.Agente.Domain.Efd.Validacao.Estrutural;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Domain.Efd.Validacao;

public sealed record ResultadoValidacao(
    IReadOnlyList<Achado> Achados,
    IReadOnlyDictionary<string, long> Totais,
    IReadOnlyList<string> RegrasExecutadas,
    IReadOnlyList<ValidacaoNaoRealizada> NaoRealizadas,
    long TotalLinhas,
    IReadOnlyList<ValidacaoNaoRealizada> ForaDoAlcance);

/// <summary>
/// Validação determinística em uma passagem. Executa só regras ativas aplicáveis; regras pendentes de
/// confirmação são listadas como não realizadas, nunca executadas (FR-030, FR-044). Não usa XSD (FR-015).
/// </summary>
public static class MotorValidacao
{
    private const string MotivoSemTabelaNcm =
        "Nenhuma versão importada da tabela de NCM cobre o período do arquivo, ou a tabela está indisponível.";

    public static async Task<ResultadoValidacao> ValidarAsync(
        Stream arquivo,
        DefinicaoLeiaute leiaute,
        CatalogoRegras catalogo,
        IdentificacaoArquivo identificacao,
        int ocorrenciasDetalhadasPorRegra,
        CancellationToken cancelamento,
        ITabelaNcm? tabelaNcm = null)
    {
        var aplicaveis = catalogo.Regras
            .Where(r => r.SeAplica(leiaute.CodVer, identificacao.PeriodoFim, identificacao.IndPerfil, identificacao.Uf))
            .ToList();
        var ativas = aplicaveis.Where(r => r.EstaAtiva).ToDictionary(r => r.Id);
        var contexto = new ContextoValidacao(leiaute, ativas, ocorrenciasDetalhadasPorRegra);

        List<IValidadorEstrutural> validadores =
        [
            new ValidadorCampos(),
            new ValidadorBlocos(),
            new ValidadorHierarquia(),
            new ValidadorIndMov(),
            new ValidadorTotalizadores(),
            new RegraUnidadeCadastrada(),
            new RegraFatorConversao(),
            new RegraTotaisDocumento(),
            new RegraTributosReforma(),
        ];

        if (tabelaNcm is not null)
        {
            validadores.Add(new RegraNcmVigente(tabelaNcm));
        }

        // Guarda todos os números do arquivo: só roda se a regra estiver ativa.
        if (ativas.ContainsKey(RegrasNegocio.LacunaNumeracao))
        {
            validadores.Add(new RegraLacunaNumeracao());
        }

        long totalLinhas = 0;
        await foreach (var linha in LeitorEfd.LerAsync(arquivo, cancelamento))
        {
            totalLinhas++;
            Processar(linha, leiaute, validadores, contexto);
        }

        foreach (var validador in validadores)
        {
            validador.Finalizar(contexto);
        }

        // Limite permanente: regra sem fonte oficial e verificação que depende de dado fora do arquivo.
        var foraDoAlcance = aplicaveis
            .Where(r => !r.EstaAtiva && r.Status == "pendente_confirmacao")
            .Select(r => new ValidacaoNaoRealizada(r.Id, Categoria.AusenciaEvidencias, "Regra pendente de confirmação em fonte oficial."))
            .ToList();
        foraDoAlcance.AddRange(contexto.ForaDoAlcance);

        // Dependência que pode voltar: impede o status concluída (FR-044, research R-30).
        var dependentes = ativas.Values.Where(r => DependeDeDadoAusente(r, tabelaNcm)).ToList();
        var naoRealizadas = dependentes
            .Where(r => r.DependeDe!.Contains(RegrasNegocio.DependenciaTabelaNcm) && tabelaNcm is null)
            .Select(r => new ValidacaoNaoRealizada(r.Id, Categoria.FalhaTecnica, MotivoSemTabelaNcm))
            .ToList();

        var executadas = ativas.Keys.Except(dependentes.Select(r => r.Id)).ToList();
        return new ResultadoValidacao(contexto.Achados, contexto.Totais, executadas, naoRealizadas, totalLinhas, foraDoAlcance);
    }

    /// <summary>Regra que depende de dado ausente não executa e não conta como executada (FR-044).</summary>
    private static bool DependeDeDadoAusente(RegraCatalogo regra, ITabelaNcm? tabelaNcm) =>
        regra.DependeDe is { } dependencias
        && dependencias.Any(d => d != RegrasNegocio.DependenciaTabelaNcm || tabelaNcm is null);

    private static void Processar(LinhaEfd linha, DefinicaoLeiaute leiaute, List<IValidadorEstrutural> validadores, ContextoValidacao contexto)
    {
        if (!linha.TerminaEmCrLf)
        {
            contexto.Emitir(RegrasEstruturais.FimDeLinha, "A linha não termina com CR e LF.", linha.Numero);
        }

        if (!linha.TemDelimitadores)
        {
            contexto.Emitir(RegrasEstruturais.Delimitador, "A linha não inicia e termina com o delimitador |.", linha.Numero);
            return;
        }

        var campos = linha.Campos();
        var definicao = leiaute.ObterRegistro(campos[0]);
        foreach (var validador in validadores)
        {
            validador.Processar(linha, campos, definicao, contexto);
        }
    }
}
