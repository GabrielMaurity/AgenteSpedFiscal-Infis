# Quickstart: Validação da EFD ICMS/IPI

Guia de execução e validação ponta a ponta da feature. Contratos em [contracts/](contracts/),
modelo em [data-model.md](data-model.md).

## Pré-requisitos

- .NET SDK 10 e Docker (com Docker Compose v2).
- Chave da OpenAI com acesso ao modelo de análise e ao modelo de embeddings definidos nos ADRs
  0002 e 0003.
- Guia Prático da EFD-ICMS/IPI 3.2.4 em PDF (fonte oficial recebida).
- Arquivos EFD sintéticos de referência em `tests/fixtures/efd/` (criados nas tarefas).

## 1. Configurar e subir o ambiente

```bash
cp .env.example .env
```

Preencha em `.env` a chave da OpenAI e as senhas dos usuários `agente` e `rag` do PostgreSQL.
O arquivo `.env` não é versionado (constitution P-19).

```bash
docker compose up -d --build
```

Esperado: containers `api-agente` (porta 8080), `api-rag` (8081), `postgres`, `mongo` e
`aspire-dashboard` (18888) em execução; `GET /health/ready` retorna 200 nas duas APIs.

## 2. Gerar tokens de desenvolvimento

As duas APIs usam a mesma chave no compose (`JWT_SIGNING_KEY_BASE64`). Antes do passo 1, copie para o
`.env` a chave exibida por:

```bash
dotnet user-jwts key --project src/Api.Agente/Api.Agente.Endpoints
```

Depois, gere um token por API, a partir do mesmo projeto, para que usem a mesma chave:

```bash
dotnet user-jwts create --project src/Api.Agente/Api.Agente.Endpoints --audience api-agente --claim org_id=org-teste --role analista --role admin_base
```

```bash
dotnet user-jwts create --project src/Api.Agente/Api.Agente.Endpoints --audience api-rag --claim org_id=org-teste --role admin_base
```

## 3. Ingerir o Guia Prático (história 4)

`POST http://localhost:8081/api/v1/documentos` (multipart) com o PDF e os metadados:
`classificacao=oficial`, `tipoDocumento=guia_pratico_efd_icms_ipi`, `titulo`, `origem`,
`versao=3.2.4`, `vigenciaInicio=2026-01-01`.

Esperado: `202` com `documentoId`; o status evolui para `ativo`. Reenviar o mesmo PDF retorna
`409` (sem duplicidade). Enviar sem `versao` ou sem `classificacao` retorna `400`.

Ingerir também um material interno de teste (`tests/fixtures/base/faq-interno.md`) com
`classificacao=interno` e `tipoDocumento=material_interno`. Esperado: `202` e status `ativo`;
`classificacao=interno` com `tipoDocumento=guia_pratico_efd_icms_ipi` retorna `400`.

## 4. Validar um arquivo (histórias 1 e 2)

`POST http://localhost:8080/api/v1/validacoes` com `arquivo=@tests/fixtures/efd/<caso>.txt`.
Esperado: `202` com `execucaoId`. Consultar `GET /api/v1/validacoes/{execucaoId}` até status final.

| Caso de referência | Resultado esperado |
|---|---|
| `valido-perfil-a.txt` | `concluida_com_limitacoes` até a US3; depois, `concluida` quando houver tabela de NCM para o período e a análise da IA for aceita. Sem achados impeditivos, `avisoPva` presente, `EFD-NEG-008` e `EFD-NEG-009` em `foraDoAlcance`, nenhuma afirmação de conformidade |
| `sem-k990.txt` | achado `erro_estrutural`, severidade `impeditiva`, registro `K990` |
| `c170-orfao.txt` | achado na linha do `C170` sem `C100` |
| `ind-mov-incoerente.txt` | achado de `IND_MOV` = `1` com registros de dados |
| `9900-divergente.txt` | achados com quantidade declarada × encontrada |
| `unidade-sem-0190.txt` | `inconsistencia_negocio` com a unidade e as linhas |
| `sem-0220.txt` | `EFD-NEG-002` no `C170`, com a unidade movimentada e a de estoque |
| `ncm-inexistente.txt` | sem tabela de NCM: `EFD-NEG-003` em `validacoesNaoRealizadas` (`falha_tecnica`); com a tabela importada no passo 4.1: achado `EFD-NEG-003`, advertência |
| `c100-divergente.txt` | `EFD-NEG-004`, `005` e `006` no `C100`, com valores comparados e diferença |
| `lacuna-numeracao-propria.txt` | nenhum achado de lacuna: `EFD-NEG-008` pendente de fonte, listada em `foraDoAlcance` |
| `lacuna-numeracao-terceiros.txt` | nenhum achado de lacuna |
| `cbs-ibs-2026.txt` | nenhum achado de `VL_DOC`; `EFD-NEG-009` em `foraDoAlcance` |
| `ficticio-completo-2026-01.txt` | nenhum achado estrutural ou de negócio |
| `periodo-2025.txt` | `falha` na etapa 3, motivo "período não suportado" |
| `periodo-2027.txt` | `falha` na etapa 3: o leiaute 021 ainda não é aceito |
| `injecao-prompt.txt` | mesmos achados do arquivo sem o texto malicioso |

Validar também: o arquivo enviado não existe mais no volume temporário após a conclusão
(FR-082), e o documento da execução tem `expiraEm` = criação + 90 dias.

**Exportação CSV (FR-048)**: `GET /api/v1/validacoes/{execucaoId}/exportacao.csv` do caso
`unidade-sem-0190.txt`. Esperado: arquivo UTF-8 com BOM, metadados `#` no topo, cabeçalho igual
ao de [exportacao-csv.md](contracts/exportacao-csv.md) e os mesmos `id` de achados do envelope.
Durante o processamento, o mesmo endpoint retorna `409`.

### 4.1 Importar a tabela de NCM (história 2)

`POST http://localhost:8080/api/v1/tabelas-referencia/ncm` (multipart, papel `admin_base`) com
`arquivo`, `origem`, `versao`, `vigenciaInicio` e, opcionalmente, `vigenciaFim`.

Esperado: `201` com `quantidadeCodigos`; reenviar a mesma versão retorna `409`; arquivo sem
código de 8 dígitos retorna `400`; papel `analista` recebe `403`. `GET` no mesmo endereço lista
as versões. `GET /api/v1/regras?status=pendente_confirmacao` lista `EFD-NEG-008` e `EFD-NEG-010`.

## 5. Dependência indisponível (P-04, SC-004)

```bash
docker compose stop api-rag
```

Reenviar `valido-perfil-a.txt`. Esperado: `concluida_com_limitacoes`, consulta à base listada em
`validacoesNaoRealizadas` com categoria `falha_tecnica`, achados determinísticos completos.

```bash
docker compose start api-rag
```

## 6. Perguntar ao agente (história 3)

Criar conversa (`POST /api/v1/conversas`) e enviar a mensagem "O valor de IBS entra no VL_DOC do
C100 em 2026?" com `periodo=2026-03`.

Esperado: `fundamentada = true` e `fundamentadaEmFonteOficial = true`; resposta indicando que,
pelo Guia 3.2.4 (Cap. I, Seção 10), a inclusão não se aplica ao exercício de 2026; `referencias`
com `trechoId` existente e `classificacao = oficial`.

Outros casos:

| Pergunta | Esperado |
|---|---|
| Fora da base (ex.: regras da ECF) | `fundamentada = false`, limitação declarada |
| Respondida só pelo `faq-interno.md` | referências com `classificacao = interno`, `fundamentadaEmFonteOficial = false` e limitação "sem fundamento em fonte oficial recuperada" |
| Em conversa vinculada a uma execução, pedindo um dado do arquivo que não está nos achados (ex.: endereço de um participante do `0150`) | `fundamentada = false`, limitação de arquivo descartado e orientação de reenvio |

## 7. Testes automatizados

```bash
dotnet test --solution AgenteSpedFiscal.slnx --filter-not-trait "Category=Docker"
```

Esperado: suíte determinística verde, sem Docker e sem chamadas reais à OpenAI.

```bash
dotnet test --solution AgenteSpedFiscal.slnx --filter-trait "Category=Docker"
```

Esperado: testes com PostgreSQL/pgvector e MongoDB reais (Testcontainers) verdes. Projetos sem esses
testes informam "Zero testes" e código de saída 8, o que é esperado.

```bash
dotnet run --project tests/AgenteSpedFiscal.Evals -- --conjunto evals/rag-guia-3.2.4
```

Avaliação sob demanda (usa a OpenAI): grava recall@k, MRR e taxa de limitação em
`evals/resultados/`, usados para fixar os parâmetros de RAG (P-10, SC-006).

## 8. Observabilidade

Abrir o Aspire Dashboard (`http://localhost:18888`) e localizar o trace da execução pelo
`execucaoId`: devem aparecer as etapas 1–9, a chamada MCP à Api.RAG e a chamada ao LLM com o mesmo
`correlacaoId` (P-18).
