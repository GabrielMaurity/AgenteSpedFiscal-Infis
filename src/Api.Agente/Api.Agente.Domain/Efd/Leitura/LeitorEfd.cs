using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Api.Agente.Domain.Efd.Leitura;

/// <summary>Linha física do arquivo. O conteúdo não é persistido nem registrado em logs (FR-082).</summary>
public sealed record LinhaEfd(long Numero, string Texto, bool TerminaEmCrLf)
{
    public bool TemDelimitadores => Texto.Length >= 2 && Texto[0] == '|' && Texto[^1] == '|';

    /// <summary>Campos entre os delimitadores; o primeiro é o código do registro.</summary>
    public string[] Campos() => Texto[1..^1].Split('|');
}

/// <summary>Leitura em fluxo, em uma passagem: a memória não cresce com o tamanho do arquivo (research R-03).</summary>
public static class LeitorEfd
{
    private const int TamanhoBuffer = 64 * 1024;

    /// <summary>ISO 8859-1 (NT 2026.001, item 2.1.1).</summary>
    public static async IAsyncEnumerable<LinhaEfd> LerAsync(Stream arquivo, [EnumeratorCancellation] CancellationToken cancelamento)
    {
        using var leitor = new StreamReader(arquivo, Encoding.Latin1, detectEncodingFromByteOrderMarks: false, TamanhoBuffer, leaveOpen: true);
        var buffer = ArrayPool<char>.Shared.Rent(TamanhoBuffer);
        var atual = new StringBuilder();
        long numero = 0;

        try
        {
            int lidos;
            while ((lidos = await leitor.ReadAsync(buffer.AsMemory(), cancelamento)) > 0)
            {
                var inicio = 0;
                for (var i = 0; i < lidos; i++)
                {
                    if (buffer[i] != '\n')
                    {
                        continue;
                    }

                    atual.Append(buffer, inicio, i - inicio);
                    inicio = i + 1;
                    yield return Montar(++numero, atual, terminouEmLf: true);
                    atual.Clear();
                }

                atual.Append(buffer, inicio, lidos - inicio);
            }

            if (atual.Length > 0)
            {
                yield return Montar(++numero, atual, terminouEmLf: false);
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static LinhaEfd Montar(long numero, StringBuilder conteudo, bool terminouEmLf)
    {
        var temCr = conteudo.Length > 0 && conteudo[^1] == '\r';
        var texto = temCr ? conteudo.ToString(0, conteudo.Length - 1) : conteudo.ToString();
        return new LinhaEfd(numero, texto, terminouEmLf && temCr);
    }
}
