using System.Security.Cryptography;
using Api.Agente.Domain.Efd.Validacao.Negocio;

namespace Api.Agente.Application.TabelasReferencia;

public sealed record TabelaNcmImportada(
    Guid Id,
    string Origem,
    string Versao,
    DateOnly VigenciaInicio,
    DateOnly? VigenciaFim,
    string Sha256,
    DateTimeOffset ImportadoEm,
    string ImportadoPor,
    int QuantidadeCodigos);

public interface IRepositorioNcm
{
    /// <summary>Grava a versão e os códigos. Retorna falso se a mesma versão já existir (FR-063).</summary>
    Task<bool> ImportarAsync(TabelaNcmImportada tabela, IReadOnlyList<LinhaNcm> linhas, CancellationToken cancelamento);

    Task<IReadOnlyList<TabelaNcmImportada>> ListarAsync(CancellationToken cancelamento);

    /// <summary>Tabela da versão vigente no período; nula se nenhuma cobrir o período ou o banco estiver indisponível.</summary>
    Task<ITabelaNcm?> CarregarVigenteAsync(DateOnly periodoInicio, DateOnly periodoFim, CancellationToken cancelamento);
}

public sealed record ComandoImportarNcm(byte[] Conteudo, string Origem, string Versao, DateOnly VigenciaInicio, DateOnly? VigenciaFim, string UsuarioId);

public sealed record ResultadoImportarNcm(TabelaNcmImportada Tabela, bool VersaoJaExistia);

/// <summary>Importação versionada: versões anteriores são mantidas para arquivos de outros períodos (FR-063).</summary>
public sealed class ImportarNcmHandler(IRepositorioNcm repositorio, TimeProvider relogio)
{
    public async Task<ResultadoImportarNcm> ImportarAsync(ComandoImportarNcm comando, CancellationToken cancelamento)
    {
        if (comando.VigenciaFim < comando.VigenciaInicio)
        {
            throw new TabelaNcmInvalidaException("O fim da vigência é anterior ao início.");
        }

        var linhas = ParserTabelaNcm.Ler(comando.Conteudo);
        var tabela = new TabelaNcmImportada(
            Guid.CreateVersion7(),
            comando.Origem,
            comando.Versao,
            comando.VigenciaInicio,
            comando.VigenciaFim,
            Convert.ToHexStringLower(SHA256.HashData(comando.Conteudo)),
            relogio.GetUtcNow(),
            comando.UsuarioId,
            linhas.Count);

        var importada = await repositorio.ImportarAsync(tabela, linhas, cancelamento);
        return new ResultadoImportarNcm(tabela, !importada);
    }
}
