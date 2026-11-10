using System.Text;
using Api.Rag.Application.Base;
using Api.Rag.Application.Ingestao;
using Api.Rag.Domain.Segmentacao;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Api.Rag.Infrastructure.Extracao;

/// <summary>
/// Extrai o texto do PDF página a página, reconstruindo as linhas pela posição das palavras: assim a
/// tabela de campos do Guia fica uma linha por campo, e a página fica disponível para a citação (research R-18).
/// </summary>
public static class ExtratorPdf
{
    /// <summary>Fração da altura da letra abaixo da qual duas palavras ficam na mesma linha.</summary>
    private const double ToleranciaLinha = 0.5;

    public static IReadOnlyList<PaginaExtraida> Extrair(byte[] conteudo)
    {
        using var documento = PdfDocument.Open(conteudo);
        return [.. documento.GetPages().Select(p => new PaginaExtraida(p.Number, Linhas(p)))];
    }

    private static IReadOnlyList<string> Linhas(Page pagina)
    {
        var linhas = new List<List<Word>>();
        double? baseAtual = null;
        foreach (var palavra in pagina.GetWords().OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var altura = Math.Max(palavra.BoundingBox.Height, 1);
            if (baseAtual is null || Math.Abs(palavra.BoundingBox.Bottom - baseAtual.Value) > altura * ToleranciaLinha)
            {
                linhas.Add([]);
                baseAtual = palavra.BoundingBox.Bottom;
            }

            linhas[^1].Add(palavra);
        }

        return [.. linhas.Select(l => string.Join(' ', l.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))];
    }
}

/// <summary>Escolhe o extrator pelo tipo detectado na recepção.</summary>
public sealed class ExtratorDocumento : IExtratorDocumento
{
    public ConteudoExtraido Extrair(ArquivoDocumento arquivo) =>
        arquivo.TipoConteudo == IngestaoHandler.TipoPdf
            ? new ConteudoExtraido(ExtratorPdf.Extrair(arquivo.Conteudo), null)
            : new ConteudoExtraido(null, ExtratorTexto.Extrair(arquivo.Conteudo));
}

/// <summary>Markdown e texto simples, já validados como UTF-8 na recepção.</summary>
public static class ExtratorTexto
{
    public static string Extrair(byte[] conteudo) => Encoding.UTF8.GetString(conteudo).TrimStart('﻿');
}
