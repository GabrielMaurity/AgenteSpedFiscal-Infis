using System.Security.Cryptography;
using Api.Agente.Application.Validacao;
using Api.Agente.Infrastructure.Configuracao;
using Microsoft.Extensions.Options;

namespace Api.Agente.Infrastructure.Arquivos;

/// <summary>Grava o upload em streaming, sem carregá-lo em memória (research R-03, FR-002, FR-082).</summary>
public sealed class ArmazenamentoTemporario(IOptions<ArquivosOpcoes> arquivos, IOptions<LimitesOpcoes> limites) : IArmazenamentoTemporario
{
    private const int TamanhoBuffer = 81_920;
    private const int BytesInspecionados = 4_096;

    public async Task<ArquivoRecebido> SalvarAsync(Stream conteudo, string nome, CancellationToken cancelamento)
    {
        if (!string.Equals(Path.GetExtension(nome), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArquivoRecusadoException(MotivoRecusaArquivo.FormatoInvalido, "Envie um arquivo com extensão .txt.");
        }

        var limite = limites.Value.TamanhoMaximoArquivoBytes;
        Directory.CreateDirectory(arquivos.Value.DiretorioTemporario);
        var caminho = Path.Combine(arquivos.Value.DiretorioTemporario, $"{Guid.NewGuid():N}.tmp");

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var destino = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.None, TamanhoBuffer, useAsync: true);

            var buffer = new byte[TamanhoBuffer];
            long total = 0;
            int lidos;
            while ((lidos = await conteudo.ReadAsync(buffer, cancelamento)) > 0)
            {
                if (total == 0)
                {
                    GarantirTexto(buffer.AsSpan(0, Math.Min(lidos, BytesInspecionados)));
                }

                total += lidos;
                if (total > limite)
                {
                    throw new ArquivoRecusadoException(
                        MotivoRecusaArquivo.TamanhoExcedido,
                        $"O arquivo excede o limite de {limite / (1024 * 1024)} MB.");
                }

                hash.AppendData(buffer, 0, lidos);
                await destino.WriteAsync(buffer.AsMemory(0, lidos), cancelamento);
            }

            if (total == 0)
            {
                throw new ArquivoRecusadoException(MotivoRecusaArquivo.FormatoInvalido, "O arquivo está vazio.");
            }

            return new ArquivoRecebido(caminho, Path.GetFileName(nome), total, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch
        {
            Excluir(caminho);
            throw;
        }
    }

    public Stream AbrirLeitura(string caminho) =>
        new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read, TamanhoBuffer, useAsync: true);

    public void Excluir(string caminho)
    {
        if (File.Exists(caminho))
        {
            File.Delete(caminho);
        }
    }

    /// <summary>Apaga sobras de um processo anterior; o conteúdo não pode ficar retido (FR-082).</summary>
    public void LimparDiretorio()
    {
        if (!Directory.Exists(arquivos.Value.DiretorioTemporario))
        {
            return;
        }

        foreach (var arquivo in Directory.EnumerateFiles(arquivos.Value.DiretorioTemporario, "*.tmp"))
        {
            File.Delete(arquivo);
        }
    }

    /// <summary>A codificação exigida é ISO 8859-1 (NT 2026.001, item 2.1.1): recusa BOM de Unicode e binários.</summary>
    private static void GarantirTexto(ReadOnlySpan<byte> inicio)
    {
        if (inicio.StartsWith<byte>([0xEF, 0xBB, 0xBF]) || inicio.StartsWith<byte>([0xFF, 0xFE]) || inicio.StartsWith<byte>([0xFE, 0xFF]))
        {
            throw new ArquivoRecusadoException(MotivoRecusaArquivo.FormatoInvalido, "O arquivo deve estar em ISO 8859-1 (Latin-1), sem marca de Unicode.");
        }

        if (inicio.Contains((byte)0))
        {
            throw new ArquivoRecusadoException(MotivoRecusaArquivo.FormatoInvalido, "O conteúdo enviado não é um arquivo de texto.");
        }
    }
}
