# Data Model: Validação da EFD ICMS/IPI (SPED Fiscal)

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

Convenções: identificadores são UUID v7 gerados pelo sistema; datas em UTC (ISO 8601); valores
monetários em `decimal`; todo registro persistido por organização contém `orgId`, e toda consulta
filtra por ele (FR-080).

---

## 1. Api.Agente — domínio de validação (em memória, por execução)

Estas entidades existem apenas durante o processamento e não são persistidas com o conteúdo do
arquivo (FR-082).

### LinhaEfd

| Campo | Tipo | Regra |
|---|---|---|
| numero | long | ≥ 1, posição física no arquivo |
| registro | string(4) | código do registro (ex.: `C100`) |
| campos | string[] | valores entre os delimitadores pipe (\|) |

### DefinicaoLeiaute (arquivo `leiaute-efd-icms-ipi-<COD_VER>.json`)

| Campo | Tipo | Regra |
|---|---|---|
| codVer | string | versão do leiaute (campo `COD_VER` do `0000`) |
| vigencia | {inicio, fim?} | períodos de apuração cobertos (NT 2026.001, item 3.1.1), comparados com `DT_FIN` |
| codificacao | string | `ISO-8859-1` (NT 2026.001, item 2.1.1; R-07) |
| blocos[] | {codigo, ordem, abertura, encerramento, obrigatorio, vigenciaInicio?} | ordem do Guia, Cap. II, Seção 1 |
| registros[] | DefinicaoRegistro | — |
| fonte | Fonte | documento, versão e localização da definição |

### DefinicaoRegistro

| Campo | Tipo | Regra |
|---|---|---|
| codigo | string(4) | único na versão |
| bloco | string(1) | — |
| nivel | int | "Nível hierárquico" do Guia |
| pai | string(4)? | nulo só para nível 0 |
| ocorrencia | `1:1` \| `1:N` | conforme Guia |
| obrigatoriedadePorPerfil | map perfil → O/OC/N | Guia, Cap. III, Seção 5. **Não implementado**; depende da decisão em aberto (research R-35) |
| campos[] | {numero, nome, tipo `N`/`C`, tamanho?, tamanhoExato, decimais?, obrigatorio `O`/`OC`} | Guia, detalhamento do registro |
| excecoesObrigatoriedade[]? | {situacoes (valores de `COD_SIT`), camposObrigatorios[], fonte} | só nos registros cuja obrigatoriedade varia com a situação do documento: `C100`, `C500`, `D100`, `D400`, `D500`, `D700`, `1500` (research R-33) |
| fonte | Fonte | página/seção do Guia |

### Fonte (objeto de valor)

| Campo | Tipo | Regra |
|---|---|---|
| documento | string | ex.: "Guia Prático da EFD-ICMS/IPI" |
| versao | string | ex.: "3.2.4" |
| localizacao | string | seção, registro, campo ou página; obrigatório |
| documentoId | uuid? | id do documento na base de conhecimento, quando ingerido |

---

## 2. Api.Agente — Catálogo de Regras (arquivo versionado)

Schema: [catalogo-regras.schema.json](contracts/catalogo-regras.schema.json).

### RegraCatalogo

| Campo | Tipo | Regra |
|---|---|---|
| id | string | formato `EFD-<ESTR\|NEG>-<nnn>`, único e imutável |
| descricao | string | — |
| tipo | `estrutural` \| `negocio` | determina a categoria do achado |
| severidade | `impeditiva` \| `advertencia` \| `informativa` | FR-042 |
| status | `ativa` \| `pendente_confirmacao` \| `revogada` | só `ativa` é executada (FR-030) |
| aplicacao | {codVer[], periodoInicio, periodoFim?, perfis[]?, ufs[]?} | — |
| fonte | Fonte | obrigatória e completa quando `status = ativa` |
| origemCandidata | string? | ex.: "documento-base, seção 6" (FR-031) |
| dependeDe | string[] | dados externos necessários. `tabela:ncm`: sem versão para o período ⇒ validação não realizada (`falha_tecnica`). `xml:documento`: nunca disponível no arquivo ⇒ item `foraDoAlcance` (`ausencia_evidencias`), research R-30 |
| acaoCorretiva | string | texto apresentado ao usuário |

### FonteOficialCatalogo (lista `fontesOficiais` do catálogo)

| Campo | Tipo | Regra |
|---|---|---|
| documento | string | nome do documento oficial (ex.: "Guia Prático da EFD-ICMS/IPI") |
| versao | string | ex.: "3.2.4" |
| ato | string? | ex.: "Ato COTEPE/ICMS nº 44/2018" |

**Regras de integridade**: build e inicialização falham se uma regra `ativa` não tiver
`fonte.documento`, `fonte.versao` e `fonte.localizacao`, ou se o par documento/versão não estiver
em `fontesOficiais`. Material interno nunca entra nessa lista (FR-030, research R-27).

---

## 3. Api.Agente — MongoDB

### Coleção `execucoes`

| Campo | Tipo | Regra |
|---|---|---|
| _id | uuid | execucaoId |
| orgId, usuarioId | string | do token |
| canal | `rest` \| `a2a` | — |
| correlacaoId | string | propagado (P-18) |
| status | ver estados | — |
| etapas[] | {etapa (1–9 da P-05), status, inicio, fim, erro?} | — |
| arquivo | {nome, tamanhoBytes, sha256, codificacaoDetectada} | sem conteúdo |
| identificacao | {codVer, periodoInicio, periodoFim, codFin, uf, cnpj (mascarado em logs), indPerfil} | de `0000` |
| regrasExecutadas | string[] | ids do catálogo |
| validacoesNaoRealizadas[] | {regraId \| etapa, motivo, categoria} | FR-044; só dependência que pode voltar ou falha técnica |
| foraDoAlcance[] | {regraId, motivo, categoria} | FR-044; regras pendentes de fonte e verificações que dependem de dado fora do arquivo (research R-30); não afeta status nem revisão humana |
| achados[] | Achado (até N por regra, R-08) | — |
| totaisPorRegra | map regraId → long | contagem real |
| analiseIa | AnaliseIa? | resultado validado do LLM |
| revisaoHumanaNecessaria | bool | FR-046 |
| versaoContrato | string | do envelope |
| criadoEm, atualizadoEm | datetime | — |
| expiraEm | datetime | criadoEm + 90 dias; índice TTL (FR-082) |

Índices: `{orgId, criadoEm}`, `{orgId, usuarioId}`, TTL em `expiraEm`.

**Estados da execução**

```text
recebida → validando_arquivo → identificando → validando → consultando_base → analisando_ia
        → validando_saida → concluida | concluida_com_limitacoes
qualquer estado → falha  (erro impeditivo de etapa 1–4, falha técnica ou reinício do serviço)
```

No envelope ([envelope-validacao.schema.json](contracts/envelope-validacao.schema.json)) os estados
intermediários aparecem como `em_processamento`, com o detalhe em `etapas`.

`concluida` só é permitido quando `validacoesNaoRealizadas` está vazia e a análise da IA foi
aceita; caso contrário, `concluida_com_limitacoes` (P-04). Itens de `foraDoAlcance` não entram
nessa conta (research R-30).

### Achado (subdocumento)

| Campo | Tipo | Regra |
|---|---|---|
| id | string | único na execução |
| categoria | `erro_estrutural` \| `inconsistencia_negocio` \| `indicio_ia` \| `ausencia_evidencias` \| `falha_tecnica` | P-03 |
| severidade | `impeditiva` \| `advertencia` \| `informativa` | `indicio_ia` nunca `impeditiva` |
| origem | `validador` \| `ia` | `erro_estrutural`/`inconsistencia_negocio` exigem `validador` |
| regraId | string? | obrigatório quando `origem = validador` |
| linha, registro, campo | long?, string?, string? | FR-043 |
| valorEncontrado, valorEsperado | string? | — |
| mensagem, acaoCorretiva | string | — |
| fonte | Fonte? | copiada do catálogo |

### AnaliseIa (subdocumento)

| Campo | Tipo | Regra |
|---|---|---|
| status | `aceita` \| `falha` | — |
| modelo | string | preenchido pelo sistema |
| explicacoes[] | {achadoId, texto, trechoIds[]} | `achadoId` existente |
| indicios[] | Achado com `categoria = indicio_ia` | — |
| limitacoes[] | string | — |
| tentativas | int | — |

### Coleção `conversas`

| Campo | Tipo | Regra |
|---|---|---|
| _id | uuid | conversaId |
| orgId, usuarioId | string | isolamento (FR-054) |
| execucaoId | uuid? | vínculo opcional |
| mensagens[] | {id, papel `usuario`/`agente`, texto, referencias[] {trechoId, classificacao}, fundamentada, fundamentadaEmFonteOficial, proximidade, limitacoes[], criadoEm} | `fundamentadaEmFonteOficial` (R-27) e `proximidade` (R-38) calculados pelo sistema |
| expiraEm | datetime | TTL, inicial 90 dias |

---

## 4. Api.Agente — PostgreSQL (banco `agente`)

### consumo_ia

| Coluna | Tipo | Regra |
|---|---|---|
| id | uuid PK | — |
| execucao_id, correlacao_id | uuid, text | — |
| org_id | text | — |
| provedor, modelo, operacao | text | `operacao`: `chat`, `embedding` |
| tentativa | int | 1 = original; > 1 = retentativa |
| tokens_entrada, tokens_saida | int NULL | nulo quando desconhecido, nunca 0 por omissão |
| origem_tokens | `informado` \| `desconhecido` | — |
| custo_estimado | numeric(18,8) NULL | — |
| moeda | char(3) | — |
| tabela_precos_id | uuid FK NULL | — |
| inicio, duracao_ms, status | timestamptz, int, text | — |

### tabela_precos

`id`, `provedor`, `modelo`, `preco_entrada_por_milhao`, `preco_saida_por_milhao`, `moeda`,
`vigente_desde`, `referencia` (URL e data de consulta), `versao_arquivo`.

### tabela_referencia e ncm

- `tabela_referencia`: `id`, `tipo` (`ncm`), `origem`, `versao`, `vigencia_inicio`,
  `vigencia_fim`, `sha256`, `importado_em`, `importado_por`; único em `(tipo, versao)`.
- `ncm`: `tabela_referencia_id` FK, `codigo` char(8), `descricao`, `vigencia_inicio`,
  `vigencia_fim`; índice `(tabela_referencia_id, codigo)`.

---

## 5. Api.RAG — PostgreSQL + pgvector (banco `rag`)

### documento

| Coluna | Tipo | Regra |
|---|---|---|
| id | uuid PK | — |
| classificacao | `oficial` \| `interno` | obrigatória (FR-060); imutável após a ingestão |
| tipo_documento | text | ex.: `guia_pratico_efd_icms_ipi`, `nota_tecnica_leiaute`, `legislacao`, `material_interno` |
| titulo, origem | text | obrigatórios (FR-060) |
| versao | text | obrigatória |
| vigencia_inicio, vigencia_fim | date NULL | períodos a que se aplica |
| sha256 | char(64) | único (deduplicação, FR-061) |
| status | `recebido` \| `processando` \| `ativo` \| `falha` \| `inativo` | — |
| modelo_embedding, dimensao | text, int | — |
| ingerido_em, ingerido_por | timestamptz, text | — |
| motivo_falha | text NULL | motivo genérico quando `status = falha`; o detalhe técnico fica só no log |

### documento_arquivo

| Coluna | Tipo | Regra |
|---|---|---|
| documento_id | uuid PK, FK de `documento` | um arquivo por documento; apagado junto com ele |
| nome, tipo_conteudo | text | `application/pdf`, `text/markdown` ou `text/plain`, detectado na recepção |
| conteudo | bytea | arquivo original, mantido para reprocessar (FR-061); documentos da base não seguem o descarte da FR-082, que vale para o arquivo EFD |

### trecho

| Coluna | Tipo | Regra |
|---|---|---|
| id | uuid PK | trechoId citado nas respostas |
| documento_id | uuid FK | — |
| ordem | int | — |
| classificacao | `oficial` \| `interno` | cópia da classificação do documento, para filtro |
| localizacao | jsonb | {capitulo, secao, registro?, paginaInicio, paginaFim}; no Capítulo III do Guia, `secao` é o bloco do leiaute (ex.: `Bloco C`) |
| texto | text | — |
| texto_hash | char(64) | único por documento |
| tsv | tsvector | gerado, dicionário `portuguese` + códigos |
| embedding | vector(1536) | dimensão conforme ADR-0002 |

Índices: HNSW (`vector_cosine_ops`) em `embedding`; GIN em `tsv`; `(documento_id, ordem)`;
`(classificacao)` para o filtro de `buscar_trechos`.

### consumo_embeddings

Mesma estrutura de `consumo_ia` (seção 4), com `documento_id` opcional.

---

## 6. Exportação CSV (visão derivada, não persistida)

Gerada sob demanda a partir do documento da coleção `execucoes` (FR-048, research R-28). Não
existe armazenamento próprio: o CSV sempre reflete o resultado atual e expira junto com a
execução. Layout em [exportacao-csv.md](contracts/exportacao-csv.md).

---

**Estados do documento**

```text
recebido → processando → ativo → inativo
                       ↘ falha → (reprocessar) → processando
```
