using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Rag.Application.Base;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Api.Rag.Tests.Infra;

/// <summary>
/// Embeddings determinísticos sem rede: cada palavra cai numa posição do vetor, então textos com palavras
/// em comum ficam próximos no cosseno. Serve para testar a busca; não substitui a avaliação (R-17).
/// </summary>
public sealed class GeradorEmbeddingsFalso : IGeradorEmbeddings
{
    private int _chamadas;

    public string Modelo => "falso-palavras";

    public int Dimensao => 1536;

    public int Chamadas => _chamadas;

    public Task<IReadOnlyList<float[]>> GerarAsync(IReadOnlyList<string> textos, Guid? documentoId, string correlacaoId, CancellationToken cancelamento)
    {
        Interlocked.Increment(ref _chamadas);
        return Task.FromResult<IReadOnlyList<float[]>>([.. textos.Select(Vetor)]);
    }

    private float[] Vetor(string texto)
    {
        var vetor = new float[Dimensao];
        foreach (var palavra in texto.ToLowerInvariant().Split([' ', '\n', '\r', '\t', ',', '.', ':', ';', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
        {
            var hash = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(palavra)), 0);
            vetor[hash % Dimensao] += 1;
        }

        vetor[0] += 0.001f;
        var norma = MathF.Sqrt(vetor.Sum(v => v * v));
        return [.. vetor.Select(v => v / norma)];
    }
}

/// <summary>Monta PDFs pequenos com PdfPig, uma linha de texto por chamada, de cima para baixo.</summary>
public static class PdfAmostra
{
    public static byte[] Gerar(IEnumerable<IEnumerable<string>> paginas)
    {
        var construtor = new PdfDocumentBuilder();
        var fonte = construtor.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var linhas in paginas)
        {
            var pagina = construtor.AddPage(PageSize.A4);
            var y = 800.0;
            foreach (var linha in linhas)
            {
                pagina.AddText(Compativel(linha), 8, new PdfPoint(30, y), fonte);
                y -= 11;
            }
        }

        return construtor.Build();
    }

    /// <summary>
    /// A fonte padrão do gerador não codifica acentos de forma recuperável: o texto vai sem acento. A extração
    /// de acentos é conferida com o PDF real do Guia, que embute as fontes.
    /// </summary>
    public static string Compativel(string linha)
    {
        var semAcento = new string([.. linha.Normalize(NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)]);
        return new string([.. semAcento.Select(c => c < 128 ? c : '-')]);
    }

    public static IReadOnlyList<IReadOnlyList<string>> PaginasDoGuia() =>
        [.. File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "base", "guia-3.2.4-amostra.txt"))
            .Split('\f')
            .Select(p => (IReadOnlyList<string>)[.. p.Split('\n').Where(l => l.Length > 0)])];
}

/// <summary>Chamadas REST de administração usadas pelos testes de ingestão e de MCP.</summary>
public static class Administracao
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static async Task<HttpResponseMessage> IngerirAsync(
        HttpClient cliente,
        byte[] conteudo,
        string nome,
        string? classificacao = "oficial",
        string? tipo = "guia_pratico_efd_icms_ipi",
        string? versao = "3.2.4",
        string? vigenciaInicio = null,
        string? vigenciaFim = null)
    {
        using var corpo = new MultipartFormDataContent();
        var arquivo = new ByteArrayContent(conteudo);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        corpo.Add(arquivo, "arquivo", nome);
        foreach (var (campo, valor) in new[]
                 {
                     ("classificacao", classificacao), ("tipoDocumento", tipo), ("titulo", $"Documento {nome}"),
                     ("origem", "Teste"), ("versao", versao), ("vigenciaInicio", vigenciaInicio), ("vigenciaFim", vigenciaFim),
                 })
        {
            if (valor is not null)
            {
                corpo.Add(new StringContent(valor), campo);
            }
        }

        return await cliente.PostAsync(new Uri("/api/v1/documentos", UriKind.Relative), corpo, Cancelamento);
    }

    public static async Task<Guid> IngerirEAguardarAsync(HttpClient cliente, byte[] conteudo, string nome, string classificacao = "oficial",
        string tipo = "guia_pratico_efd_icms_ipi", string versao = "3.2.4", string? vigenciaInicio = null, string? vigenciaFim = null)
    {
        var resposta = await IngerirAsync(cliente, conteudo, nome, classificacao, tipo, versao, vigenciaInicio, vigenciaFim);
        Assert.Equal(HttpStatusCode.Accepted, resposta.StatusCode);
        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(Cancelamento));
        var id = corpo.RootElement.GetProperty("documentoId").GetGuid();
        Assert.Equal("ativo", await AguardarAsync(cliente, id));
        return id;
    }

    /// <summary>Espera o processamento terminar e devolve o status final.</summary>
    public static async Task<string> AguardarAsync(HttpClient cliente, Guid id)
    {
        for (var tentativa = 0; tentativa < 150; tentativa++)
        {
            using var documento = JsonDocument.Parse(await cliente.GetStringAsync(new Uri($"/api/v1/documentos/{id}", UriKind.Relative), Cancelamento));
            var status = documento.RootElement.GetProperty("status").GetString()!;
            if (status is "ativo" or "falha" or "inativo")
            {
                return status;
            }

            await Task.Delay(100, Cancelamento);
        }

        throw new TimeoutException("O documento não terminou de processar.");
    }
}
