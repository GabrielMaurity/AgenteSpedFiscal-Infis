using System.Text.RegularExpressions;
using Api.Rag.Domain.Documentos;

namespace Api.Rag.Domain.Segmentacao;

/// <summary>Página extraída com as linhas já reconstruídas na ordem de leitura.</summary>
public sealed record PaginaExtraida(int Numero, IReadOnlyList<string> Linhas);

/// <summary>Valores iniciais para avaliação, ainda não aprovados (P-10, research R-13).</summary>
public sealed record ParametrosSegmentacao(int LimiteTokens = 800, int SobreposicaoParagrafos = 1)
{
    /// <summary>Estimativa sem tokenizador: cerca de 4 caracteres por token em português.</summary>
    public static int EstimarTokens(string texto) => (texto.Length + 3) / 4;
}

internal sealed record LinhaPaginada(string Texto, int Pagina);

/// <summary>Agrupa parágrafos em trechos até o limite, repetindo o último parágrafo no trecho seguinte.</summary>
internal static class Agrupador
{
    public static IEnumerable<IReadOnlyList<LinhaPaginada>> Agrupar(
        IReadOnlyList<IReadOnlyList<LinhaPaginada>> paragrafos, ParametrosSegmentacao parametros)
    {
        var atual = new List<IReadOnlyList<LinhaPaginada>>();
        var tokens = 0;
        foreach (var paragrafo in paragrafos)
        {
            var tamanho = ParametrosSegmentacao.EstimarTokens(string.Join("\n", paragrafo.Select(l => l.Texto)));
            if (atual.Count > parametros.SobreposicaoParagrafos && tokens + tamanho > parametros.LimiteTokens)
            {
                yield return [.. atual.SelectMany(p => p)];
                atual = [.. atual.TakeLast(parametros.SobreposicaoParagrafos)];
                tokens = atual.Sum(p => ParametrosSegmentacao.EstimarTokens(string.Join("\n", p.Select(l => l.Texto))));
            }

            atual.Add(paragrafo);
            tokens += tamanho;
        }

        if (atual.Count > 0)
        {
            yield return [.. atual.SelectMany(p => p)];
        }
    }
}

/// <summary>
/// Segmenta o Guia Prático pela estrutura Capítulo → Seção → Registro (research R-18). No Capítulo III, a
/// seção é o bloco do leiaute ("BLOCO C: ..."), que vira "Bloco C" na localização. Cada registro gera
/// um trecho com o título, a descrição e a tabela de campos inteira, que nunca é dividida; as notas de
/// preenchimento e validação seguem em trechos do mesmo registro. Cabeçalho e rodapé de página são removidos.
/// </summary>
public static partial class SegmentadorGuia
{
    private const double FracaoRepeticaoCabecalho = 0.6;
    private const int PaginasMinimasParaRepeticao = 3;

    public static IReadOnlyList<Trecho> Segmentar(IReadOnlyList<PaginaExtraida> paginas, ParametrosSegmentacao parametros)
    {
        var linhas = RemoverCabecalhosERodapes(paginas);
        var trechos = new List<Trecho>();
        string? capitulo = null;
        string? secao = null;
        string? registro = null;
        var unidade = new List<LinhaPaginada>();

        void Fechar()
        {
            if (unidade.Count > 0)
            {
                Emitir(trechos, unidade, capitulo, secao, registro, parametros);
            }

            unidade = [];
        }

        foreach (var linha in linhas)
        {
            if (Capitulo().Match(linha.Texto) is { Success: true } c)
            {
                Fechar();
                (capitulo, secao, registro) = (c.Groups[1].Value, null, null);
            }
            else if (Secao().Match(linha.Texto) is { Success: true } s)
            {
                Fechar();
                (secao, registro) = (s.Groups[1].Value, null);
            }
            else if (Bloco().Match(linha.Texto) is { Success: true } b)
            {
                Fechar();
                (secao, registro) = ($"Bloco {b.Groups[1].Value}", null);
            }
            else if (Registro().Match(linha.Texto) is { Success: true } r)
            {
                Fechar();
                registro = r.Groups[1].Value;
            }

            unidade.Add(linha);
        }

        Fechar();
        return Deduplicar(trechos);
    }

    private static void Emitir(
        List<Trecho> trechos, List<LinhaPaginada> unidade, string? capitulo, string? secao, string? registro, ParametrosSegmentacao parametros)
    {
        if (registro is null)
        {
            // Texto fora de registro: cada linha é um parágrafo, porque o PDF não marca parágrafos.
            foreach (var grupo in Agrupador.Agrupar([.. unidade.Select(l => (IReadOnlyList<LinhaPaginada>)[l])], parametros))
            {
                Adicionar(trechos, grupo, capitulo, secao, null);
            }

            return;
        }

        var inicioNotas = unidade.FindIndex(l => NotaDeCampo().IsMatch(l.Texto));
        var principal = inicioNotas < 0 ? unidade : unidade[..inicioNotas];
        Adicionar(trechos, principal, capitulo, secao, registro);
        if (inicioNotas < 0)
        {
            return;
        }

        // Cada "Campo NN (...)" abre um parágrafo de nota; os parágrafos seguem o limite de tamanho.
        var paragrafos = new List<IReadOnlyList<LinhaPaginada>>();
        foreach (var linha in unidade[inicioNotas..])
        {
            if (NotaDeCampo().IsMatch(linha.Texto) || paragrafos.Count == 0)
            {
                paragrafos.Add(new List<LinhaPaginada>());
            }

            ((List<LinhaPaginada>)paragrafos[^1]).Add(linha);
        }

        foreach (var grupo in Agrupador.Agrupar(paragrafos, parametros))
        {
            Adicionar(trechos, grupo, capitulo, secao, registro);
        }
    }

    private static void Adicionar(List<Trecho> trechos, IReadOnlyList<LinhaPaginada> linhas, string? capitulo, string? secao, string? registro)
    {
        var texto = string.Join("\n", linhas.Select(l => l.Texto)).Trim();
        if (texto.Length == 0)
        {
            return;
        }

        trechos.Add(new Trecho(
            trechos.Count + 1,
            texto,
            new LocalizacaoTrecho(capitulo, secao, registro, linhas.Min(l => l.Pagina), linhas.Max(l => l.Pagina))));
    }

    /// <summary>Remove a numeração de página e as linhas que se repetem na maior parte das páginas.</summary>
    private static List<LinhaPaginada> RemoverCabecalhosERodapes(IReadOnlyList<PaginaExtraida> paginas)
    {
        var repetidas = new HashSet<string>(StringComparer.Ordinal);
        if (paginas.Count >= PaginasMinimasParaRepeticao)
        {
            var minimo = Math.Ceiling(paginas.Count * FracaoRepeticaoCabecalho);
            repetidas = paginas
                .SelectMany(p => p.Linhas.Select(l => l.Trim()).Distinct())
                .GroupBy(l => l, StringComparer.Ordinal)
                .Where(g => g.Key.Length > 0 && g.Count() >= minimo)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);
        }

        return [.. paginas.SelectMany(p => p.Linhas
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !NumeroDePagina().IsMatch(l) && !repetidas.Contains(l))
            .Select(l => new LinhaPaginada(l, p.Numero)))];
    }

    /// <summary>Trechos de texto idêntico (após normalização) são guardados uma vez só (research R-18).</summary>
    internal static IReadOnlyList<Trecho> Deduplicar(IEnumerable<Trecho> trechos)
    {
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var unicos = new List<Trecho>();
        foreach (var trecho in trechos)
        {
            if (vistos.Add(trecho.TextoHash))
            {
                unicos.Add(trecho with { Ordem = unicos.Count + 1 });
            }
        }

        return unicos;
    }

    [GeneratedRegex(@"^Cap[íi]tulo\s+([IVXLC]+)\b")]
    private static partial Regex Capitulo();

    [GeneratedRegex(@"^Se[çc][ãa]o\s+(\d+)\b")]
    private static partial Regex Secao();

    [GeneratedRegex(@"^BLOCO\s+([0-9A-Z])\s*[:–-]")]
    private static partial Regex Bloco();

    [GeneratedRegex(@"^REGISTRO\s+([0-9A-Z]{4})\s*:")]
    private static partial Regex Registro();

    [GeneratedRegex(@"^Campo\s+\d{1,2}\s*\(")]
    private static partial Regex NotaDeCampo();

    [GeneratedRegex(@"^P[áa]gina\s+\d+\s+de\s+\d+$")]
    private static partial Regex NumeroDePagina();
}

/// <summary>Material sem a estrutura do Guia (Markdown ou texto): títulos e parágrafos separados por linha em branco.</summary>
public static partial class SegmentadorGenerico
{
    public static IReadOnlyList<Trecho> Segmentar(string texto, ParametrosSegmentacao parametros)
    {
        var trechos = new List<Trecho>();
        string? titulo = null;
        var paragrafos = new List<IReadOnlyList<LinhaPaginada>>();
        var paragrafo = new List<LinhaPaginada>();

        void FecharParagrafo()
        {
            if (paragrafo.Count > 0)
            {
                paragrafos.Add(paragrafo);
            }

            paragrafo = [];
        }

        void FecharSecao()
        {
            FecharParagrafo();
            foreach (var grupo in Agrupador.Agrupar(paragrafos, parametros))
            {
                var conteudo = string.Join("\n", grupo.Select(l => l.Texto)).Trim();
                if (conteudo.Length > 0)
                {
                    trechos.Add(new Trecho(trechos.Count + 1, conteudo, new LocalizacaoTrecho(null, titulo, null, null, null)));
                }
            }

            paragrafos = [];
        }

        foreach (var bruta in texto.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var linha = bruta.TrimEnd();
            if (Titulo().Match(linha) is { Success: true } t)
            {
                FecharSecao();
                titulo = t.Groups[1].Value.Trim();
                paragrafo.Add(new LinhaPaginada(linha, 0));
                continue;
            }

            if (linha.Trim().Length == 0)
            {
                FecharParagrafo();
                continue;
            }

            paragrafo.Add(new LinhaPaginada(linha, 0));
        }

        FecharSecao();
        return SegmentadorGuia.Deduplicar(trechos);
    }

    [GeneratedRegex(@"^#{1,6}\s+(.+)$")]
    private static partial Regex Titulo();
}
