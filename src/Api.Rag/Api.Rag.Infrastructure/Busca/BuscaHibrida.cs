using System.Text.RegularExpressions;
using Api.Rag.Application.Base;
using Api.Rag.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using DominioClassificacao = Api.Rag.Domain.Documentos.ClassificacaoDocumento;

namespace Api.Rag.Infrastructure.Busca;

/// <summary>
/// Busca híbrida (research R-13, R-27): cosseno no pgvector, texto em português e correspondência exata de
/// códigos como C190 e VL_OPR, fundidos por Reciprocal Rank Fusion. Os filtros de status, classificação,
/// tipo, versão ou período e modelo de embedding valem antes de qualquer ranqueamento (P-09).
/// </summary>
public sealed partial class BuscaHibrida(RagDbContext banco) : IBuscaTrechos
{
    /// <summary>Constante usual do RRF: reduz o peso de quem aparece só no fim de uma lista.</summary>
    private const int ConstanteRrf = 60;
    private const int CandidatosMinimos = 40;
    private const int CodigosMaximos = 5;

    /// <summary>Propriedades de inicialização, e não construtor: o EF só traduz acesso a membros nesse formato.</summary>
    private sealed class Candidato
    {
        public required TrechoEntidade Trecho { get; init; }
        public required DocumentoEntidade Documento { get; init; }
    }

    public async Task<IReadOnlyList<TrechoEncontrado>> BuscarAsync(ConsultaTrechos consulta, CancellationToken cancelamento)
    {
        var candidatos = Math.Max(consulta.TopK * 5, CandidatosMinimos);
        var filtrados = Filtrar(consulta);
        var vetor = new Vector(consulta.Vetor);

        var listas = new List<IReadOnlyList<Guid>>
        {
            await filtrados
                .OrderBy(c => c.Trecho.Embedding.CosineDistance(vetor))
                .Select(c => c.Trecho.Id)
                .Take(candidatos)
                .ToListAsync(cancelamento),
            await filtrados
                .Where(c => c.Trecho.Tsv!.Matches(EF.Functions.WebSearchToTsQuery("portuguese", consulta.Texto)))
                .OrderByDescending(c => c.Trecho.Tsv!.Rank(EF.Functions.WebSearchToTsQuery("portuguese", consulta.Texto)))
                .Select(c => c.Trecho.Id)
                .Take(candidatos)
                .ToListAsync(cancelamento),
        };

        foreach (var codigo in Codigos(consulta.Texto))
        {
            var registro = $$"""{"registro":"{{codigo}}"}""";
            listas.Add(await filtrados
                .Where(c => EF.Functions.JsonContains(c.Trecho.Localizacao, registro))
                .OrderBy(c => c.Trecho.Ordem)
                .Select(c => c.Trecho.Id)
                .Take(candidatos)
                .ToListAsync(cancelamento));
            listas.Add(await filtrados
                .Where(c => c.Trecho.Texto.Contains(codigo))
                .OrderBy(c => c.Trecho.Ordem)
                .Select(c => c.Trecho.Id)
                .Take(candidatos)
                .ToListAsync(cancelamento));
        }

        var pontuacoes = new Dictionary<Guid, double>();
        foreach (var lista in listas)
        {
            for (var posicao = 0; posicao < lista.Count; posicao++)
            {
                pontuacoes[lista[posicao]] = pontuacoes.GetValueOrDefault(lista[posicao]) + 1.0 / (ConstanteRrf + posicao + 1);
            }
        }

        var melhores = pontuacoes.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(consulta.TopK).ToList();
        var ids = melhores.Select(m => m.Key).ToList();
        var carregados = await banco.Trechos.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Join(banco.Documentos.AsNoTracking(), t => t.DocumentoId, d => d.Id, (t, d) => new Candidato { Trecho = t, Documento = d })
            .ToDictionaryAsync(c => c.Trecho.Id, cancelamento);

        // Similaridade de cosseno só dos trechos devolvidos: a pontuação RRF ordena, mas não fica entre 0 e 1.
        var distancias = await banco.Trechos.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, Distancia = t.Embedding.CosineDistance(vetor) })
            .ToDictionaryAsync(t => t.Id, t => t.Distancia, cancelamento);

        return [.. melhores.Select(m => Montar(carregados[m.Key], m.Value, Similaridade(distancias[m.Key])))];
    }

    public async Task<TrechoEncontrado?> ObterAsync(Guid trechoId, CancellationToken cancelamento)
    {
        var candidato = await banco.Trechos.AsNoTracking()
            .Where(t => t.Id == trechoId)
            .Join(banco.Documentos.AsNoTracking(), t => t.DocumentoId, d => d.Id, (t, d) => new Candidato { Trecho = t, Documento = d })
            .Where(c => c.Documento.Status == StatusDocumento.Ativo)
            .FirstOrDefaultAsync(cancelamento);
        return candidato is null ? null : Montar(candidato, 1, null);
    }

    /// <summary>Oficial: tipo e versão, ou vigência no período. Interno: vigência, quando há período (research R-27).</summary>
    private IQueryable<Candidato> Filtrar(ConsultaTrechos consulta)
    {
        var oficial = consulta.Classificacoes.Contains(DominioClassificacao.Oficial);
        var interno = consulta.Classificacoes.Contains(DominioClassificacao.Interno);
        var (tipo, versao, inicio, fim, modelo) = (consulta.TipoDocumento, consulta.Versao, consulta.PeriodoInicio, consulta.PeriodoFim, consulta.ModeloEmbedding);

        return banco.Trechos.AsNoTracking()
            .Join(banco.Documentos.AsNoTracking(), t => t.DocumentoId, d => d.Id, (t, d) => new Candidato { Trecho = t, Documento = d })
            .Where(c => c.Documento.Status == StatusDocumento.Ativo && c.Documento.ModeloEmbedding == modelo)
            .Where(c =>
                (oficial
                    && c.Documento.Classificacao == ClassificacaoDocumento.Oficial
                    && c.Documento.TipoDocumento == tipo
                    && (versao != null
                        ? c.Documento.Versao == versao
                        : (c.Documento.VigenciaInicio == null || c.Documento.VigenciaInicio <= fim)
                            && (c.Documento.VigenciaFim == null || c.Documento.VigenciaFim >= inicio)))
                || (interno
                    && c.Documento.Classificacao == ClassificacaoDocumento.Interno
                    && (inicio == null
                        || ((c.Documento.VigenciaInicio == null || c.Documento.VigenciaInicio <= fim)
                            && (c.Documento.VigenciaFim == null || c.Documento.VigenciaFim >= inicio)))));
    }

    /// <summary>Códigos de registro (C190, 0200) e de campo (VL_OPR, COD_SIT) citados na consulta.</summary>
    internal static IReadOnlyList<string> Codigos(string texto) =>
        [.. Codigo().Matches(texto).Select(m => m.Value).Where(c => c.Any(char.IsAsciiDigit) || c.Contains('_', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).Take(CodigosMaximos)];

    private static TrechoEncontrado Montar(Candidato c, double pontuacao, double? similaridade) => new(
        c.Trecho.Id,
        c.Trecho.Texto,
        MapeamentoDocumento.ParaDominio(c.Documento),
        MapeamentoDocumento.Localizacao(c.Trecho.Localizacao),
        pontuacao,
        similaridade);

    /// <summary>1 − distância de cosseno, limitada a [0, 1] (a distância vai de 0 a 2).</summary>
    internal static double Similaridade(double distancia) => Math.Round(Math.Clamp(1 - distancia, 0, 1), 4);

    [GeneratedRegex(@"\b(?:[0-9A-Z]{4}|[A-Z]{2,}(?:_[A-Z0-9]+)+)\b")]
    private static partial Regex Codigo();
}
