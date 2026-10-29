# Contrato — Exportação do resultado em CSV (FR-048)

**Versão do contrato**: 1.0.0 | **Endpoint**: `GET /api/v1/validacoes/{execucaoId}/exportacao.csv`
([api-agente.openapi.yaml](api-agente.openapi.yaml)) | **Decisões**: research R-28

## Formato

- Codificação UTF-8 com BOM; quebra de linha CRLF.
- Separador `;`; números com vírgula decimal; datas em ISO 8601.
- Aspas duplas conforme RFC 4180 quando o valor contém `;`, aspas ou quebra de linha.
- Proteção contra injeção de fórmulas: valores que começam com `=`, `+`, `-`, `@`, tabulação ou
  retorno de carro recebem o prefixo `'`.
- Disponível apenas para execuções em status final (`concluida`, `concluida_com_limitacoes`,
  `falha`); caso contrário, `409`.

## Estrutura

1. **Metadados** — linhas iniciadas por `#`, no formato `# chave;valor`, nesta ordem:
   `versaoContrato`, `execucaoId`, `correlacaoId`, `status`, `criadoEm`, `concluidoEm`,
   `arquivoNome`, `arquivoSha256`, `codVer`, `periodoInicio`, `periodoFim`, `codFin`, `uf`,
   `indPerfil`, `cnpj`, `revisaoHumanaNecessaria`, `regrasExecutadas` (ids separados por `,`),
   `validacoesNaoRealizadas` (uma linha `# validacaoNaoRealizada;<referencia>;<categoria>;<motivo>`
   por item) e `avisoPva` (texto fixo do envelope).
2. **Cabeçalho de achados** — exatamente:

   ```text
   id;categoria;severidade;origem;linha;registro;campo;valorEncontrado;valorEsperado;regraId;fonteDocumento;fonteVersao;fonteLocalizacao;mensagem;acaoCorretiva
   ```

3. **Achados** — uma linha por achado detalhado do resultado, na mesma ordem da API (ordenação por
   linha e, em seguida, por `regraId`). Indícios da IA aceitos aparecem com `origem = ia` e campos
   de fonte vazios.
4. **Totais** — quando alguma regra tem mais ocorrências do que as detalhadas (research R-08), uma
   linha em branco seguida de `# totais` e uma linha `# total;<regraId>;<ocorrenciasTotais>;<ocorrenciasDetalhadas>`
   por regra afetada.

## Regras verificáveis

- O conjunto de `id` das linhas de achados é idêntico ao conjunto de `achados[].id` do envelope da
  mesma execução (SC-003).
- Nenhum conteúdo do arquivo EFD além dos valores já presentes nos achados e na identificação é
  incluído (FR-082).
- Após a expiração ou exclusão da execução, o endpoint retorna `404`.
