using System.Text;
using System.Text.Json;
using Api.Agente.Domain.Achados;
using Api.Agente.Domain.Efd;
using Api.Agente.Domain.Efd.Catalogo;
using Api.Agente.Domain.Efd.Identificacao;
using Api.Agente.Domain.Efd.Leitura;
using Api.Agente.Domain.Efd.Validacao;
using Api.Agente.Domain.Efd.Validacao.Negocio;
using Api.Agente.Domain.Execucoes;

namespace Api.Agente.Domain.Tests.Estrutural;

public sealed record AchadoEsperado(string RegraId, string? Registro, long? Linha, string? Campo);

public sealed record Gabarito(string Status, List<AchadoEsperado>? Achados, List<AchadoEsperado>? AchadosContem, int? Etapa, string? MotivoContem);

/// <summary>Arquivos sintéticos de tests/fixtures/efd, gerados por tools/gerar-fixtures-efd/gerar.py.</summary>
public static class FixturesEfd
{
    public static readonly DadosEfd Dados = DadosEfd.Carregar();

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Caminho(string arquivo) => Path.Combine(AppContext.BaseDirectory, "fixtures", "efd", arquivo);

    public static Stream Abrir(string caso) => File.OpenRead(Caminho($"{caso}.txt"));

    public static Gabarito LerGabarito(string caso) =>
        JsonSerializer.Deserialize<Gabarito>(File.ReadAllText(Caminho($"{caso}.gabarito.json")), Json)!;

    public static async Task<ResultadoIdentificacao> IdentificarAsync(Stream arquivo, CancellationToken cancelamento)
    {
        await foreach (var linha in LeitorEfd.LerAsync(arquivo, cancelamento))
        {
            arquivo.Position = 0;
            return IdentificadorArquivo.Identificar(linha);
        }

        return IdentificadorArquivo.Identificar(null);
    }

    public static async Task<ResultadoValidacao> ValidarAsync(
        Stream arquivo,
        CancellationToken cancelamento,
        int limite = 1_000,
        ITabelaNcm? tabelaNcm = null,
        CatalogoRegras? catalogo = null)
    {
        var identificacao = (await IdentificarAsync(arquivo, cancelamento)).Identificacao!;
        return await MotorValidacao.ValidarAsync(arquivo, Dados.Leiautes[0], catalogo ?? Dados.Catalogo, identificacao, limite, cancelamento, tabelaNcm);
    }

    public static Task<ResultadoValidacao> ValidarCasoAsync(
        string caso,
        CancellationToken cancelamento,
        ITabelaNcm? tabelaNcm = null,
        CatalogoRegras? catalogo = null) =>
        ValidarAsync(Abrir(caso), cancelamento, tabelaNcm: tabelaNcm, catalogo: catalogo);

    public static async Task<string> LerTextoAsync(string caso, CancellationToken cancelamento) =>
        Encoding.Latin1.GetString(await File.ReadAllBytesAsync(Caminho($"{caso}.txt"), cancelamento));

    public static MemoryStream ComoArquivo(string texto) => new(Encoding.Latin1.GetBytes(texto));

    public static string Trocar(string texto, string de, string para)
    {
        Assert.Contains(de, texto, StringComparison.Ordinal);
        return texto.Replace(de, para, StringComparison.Ordinal);
    }

    public static string Descrever(IEnumerable<Achado> achados) =>
        string.Join("\n", achados.Select(a => $"{a.RegraId} L{a.Linha} {a.Registro}/{a.Campo}: {a.Mensagem}"));

    public static bool Corresponde(Achado achado, AchadoEsperado esperado) =>
        achado.RegraId == esperado.RegraId
        && (esperado.Registro is null || achado.Registro == esperado.Registro)
        && (esperado.Linha is null || achado.Linha == esperado.Linha)
        && (esperado.Campo is null || achado.Campo == esperado.Campo);
}
