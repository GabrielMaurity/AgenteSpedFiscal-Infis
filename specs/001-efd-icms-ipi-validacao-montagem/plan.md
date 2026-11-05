# Implementation Plan: Validação da EFD ICMS/IPI (SPED Fiscal)

**Branch**: `001-efd-icms-ipi-validacao-montagem` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-efd-icms-ipi-validacao-montagem/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Pré-validador de arquivos EFD ICMS/IPI (períodos a partir de 01/2026, até 500 MB) com base de
informação consultável (documentos oficiais, a começar pelo Guia Prático 3.2.4, e material interno
da Infis, sempre classificados). O agente não gera arquivos SPED. O resultado pode ser baixado em
CSV. A `Api.Agente` recebe o arquivo em streaming, executa de forma
assíncrona um motor de validação determinístico orientado por dados (leiaute versionado e Catálogo
de Regras com fonte oficial obrigatória), consulta a `Api.RAG` por MCP e usa o LLM da OpenAI, via
Semantic Kernel e saída estruturada estrita, apenas para explicar achados e apontar indícios. A
`Api.RAG` ingere documentos oficiais e internos com classificação obrigatória, segmenta pela
estrutura do Guia e faz busca híbrida (pgvector + texto) filtrada por versão e classificação; as
respostas sinalizam a origem de cada citação (R-27). Tudo roda em Docker com PostgreSQL/pgvector e MongoDB.
Decisões em [research.md](research.md).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (LTS), ASP.NET Core Minimal APIs

**Primary Dependencies**: Semantic Kernel (conector OpenAI); SDK C# do Model Context Protocol
(`ModelContextProtocol`, `ModelContextProtocol.AspNetCore`); SDK .NET do A2A; EF Core 10 + Npgsql
+ `Pgvector.EntityFrameworkCore`; MongoDB.Driver; UglyToad.PdfPig;
`Microsoft.Extensions.Http.Resilience`; OpenTelemetry. Versões fixadas em
`Directory.Packages.props` no início da implementação.

**Storage**: MongoDB 8 (execuções e conversas, TTL 90 dias); PostgreSQL 17 banco `agente`
(consumo, preços, tabelas de referência/NCM); PostgreSQL 17 + pgvector banco `rag` (documentos,
trechos, embeddings, consumo de embeddings). Arquivo EFD apenas em volume temporário durante o
processamento.

**Testing**: xUnit v3, Testcontainers (PostgreSQL/pgvector, MongoDB), `WebApplicationFactory`,
JsonSchema.Net (contratos), NetArchTest (dependências), projeto de avaliação
`AgenteSpedFiscal.Evals` fora da suíte de PR.

**Target Platform**: containers Linux (imagens `aspnet:10.0` chiseled, usuário não root) via
Docker Compose em desenvolvimento.

**Project Type**: dois web services (Api.Agente e Api.RAG) + biblioteca de contratos MCP.

**Performance Goals**: relatório determinístico em ≤ 2 min para arquivos de até 50 MB e ≤ 15 min
para até 500 MB (SC-005).

**Constraints**: leitura em fluxo com memória limitada pelos cadastros (R-03); arquivo descartado
ao fim (FR-082); nenhuma consulta externa durante a validação determinística (FR-022); validação
funciona sem IA e sem RAG (FR-084); LLM recebe resumo de achados, nunca o arquivo inteiro.

**Scale/Scope**: EFD ICMS/IPI, um leiaute (vigente para 2026), todos os registros do leiaute na
validação estrutural, regras de negócio das seções 3 e 6 do documento-base após confirmação no
Guia; múltiplas organizações isoladas por `org_id`.

**Fontes oficiais recebidas**: Guia Prático 3.2.4 e NT 2026.001 v1.0, que traz a Tabela Versão do
Leiaute (item 3.1.1) e define a codificação ISO 8859-1 (item 2.1.1). Não há bloqueio externo nem
itens NEEDS CLARIFICATION técnicos em aberto.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio | Como o plano atende | Pré | Pós |
|---|---|---|---|
| P-01 Fontes oficiais | Catálogo com `fonte` obrigatória validada no build e restrita à lista `fontesOficiais` (R-06, R-27); leiaute com fonte por registro (R-05); material interno nunca é fonte de regra | ✅ | ✅ |
| P-02 Determinismo antes do LLM | Motor de validação sem LLM; LLM não emite achados determinísticos (R-10); sem XSD | ✅ | ✅ |
| P-03 Categorias | Enum fechado no envelope e regras `if/then` de origem (contracts) | ✅ | ✅ |
| P-04 Sem conformidade com pendências | `concluida` só sem pendências; `avisoPva` constante; teste com `api-rag` parado (quickstart §5) | ✅ | ✅ |
| P-05 Fluxo de 9 etapas | `etapas[]` 1–9 na execução e no envelope; interrupção nas etapas 1–4 | ✅ | ✅ |
| P-06 (revogado na 2.0.0) | Geração de SPED fora do escopo (constitution 1.3) | N/A | N/A |
| P-07 Fronteiras e dados | MCP como único canal funcional; bancos e credenciais separados (R-19); NCM na Api.Agente (R-20) | ✅ | ✅ |
| P-08 Camadas e simplicidade | 4 projetos por serviço + contratos MCP; NetArchTest; ver Complexity Tracking | ✅ | ✅ |
| P-09 Base de informação classificada | `documento`/`trecho` com classificação, versão, vigência e localização; filtro por versão obrigatório; citações com classificação | ✅ | ✅ |
| P-10 Parâmetros de RAG avaliados | Valores iniciais apenas para avaliação; fixados após `Evals` (R-13, R-17) | ✅ | ✅ |
| P-11 Saída estruturada | `llm-analise.schema.json` versionado, modo estrito, até 2 retentativas | ✅ | ✅ |
| P-12 Metadados do sistema | Envelope separado do conteúdo do LLM; modelo, consumo e `fundamentadaEmFonteOficial` preenchidos pelo sistema | ✅ | ✅ |
| P-13 Sem fabricação | Validação semântica cruza `achadoId` e `trechoId` com a execução; sem acesso ao arquivo descartado nas conversas (R-29) | ✅ | ✅ |
| P-14 Conteúdo não confiável | Conteúdo delimitado como dado no prompt; caso `injecao-prompt.txt`; evals de injeção | ✅ | ✅ |
| P-15 Ferramentas governadas | Ferramentas só leitura; `IFunctionInvocationFilter` com allow-list, validação, timeout e log (R-11) | ✅ | ✅ |
| P-16 Memória | MongoDB (ADR-0001), isolamento por `org_id`/usuário, TTL, limite de 20 mensagens ao modelo | ✅ | ✅ |
| P-17 Consumo e custos | `consumo_ia`/`consumo_embeddings` por chamada e tentativa; nulo = desconhecido (R-21) | ✅ | ✅ |
| P-18 Rastreabilidade | `execucaoId`/`correlacaoId` em REST, MCP, A2A, LLM, consumo e CSV exportado; OpenTelemetry; mascaramento | ✅ | ✅ |
| P-19 Configuração externa | `appsettings` + variáveis de ambiente; `.env` não versionado; validação na inicialização | ✅ | ✅ |
| P-20 A2A | Agent Card com 2 skills chamando os mesmos casos de uso (R-16) | ✅ | ✅ |
| P-21 Especificação antes | spec + clarificações concluídas; ADRs listados abaixo antes das tarefas correspondentes | ✅ | ✅ |
| P-22 Testes | Suíte determinística sem OpenAI; fixtures com gabarito; evals separados (R-24) | ✅ | ✅ |

**Resultado**: aprovado, sem violações. Itens de complexidade justificados abaixo.

**Alinhamento com a constitution 2.0.0**: a P-06 (geração) foi revogada e a seção 1.3 proíbe
gerar, corrigir, assinar ou transmitir SPED, o que coincide com o escopo desta feature. A P-09
passou a exigir a classificação oficial/interno, já atendida por R-27, pelo data-model e pelos
contratos.

**ADRs a registrar** (em `docs/adr/`, antes da tarefa correspondente): ADR-0001 MongoDB para
memória e execuções; ADR-0002 embeddings e busca híbrida; ADR-0003 modelo de análise (após evals);
ADR-0004 provedor OIDC; ADR-0005 MCP Streamable HTTP; ADR-0006 A2A; ADR-0007 separação dos
bancos PostgreSQL.

## Project Structure

### Documentation (this feature)

```text
specs/001-efd-icms-ipi-validacao-montagem/
├── plan.md              # Este arquivo
├── research.md          # Fase 0
├── data-model.md        # Fase 1
├── quickstart.md        # Fase 1
├── contracts/           # Fase 1
│   ├── api-agente.openapi.yaml
│   ├── api-rag.openapi.yaml
│   ├── mcp-api-rag.md
│   ├── llm-analise.schema.json
│   ├── envelope-validacao.schema.json
│   ├── catalogo-regras.schema.json
│   ├── exportacao-csv.md
│   └── a2a-agent-card.json
├── checklists/requirements.md
├── referencias/documento-base-usuario.md
└── tasks.md             # Fase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
AgenteSpedFiscal.slnx
global.json                        # SDK .NET 10 e Microsoft.Testing.Platform
dotnet-tools.json                  # dotnet-ef
Directory.Build.props              # Nullable, analisadores, TreatWarningsAsErrors
Directory.Packages.props           # versões centralizadas
docker-compose.yml
.env.example
data/
└── precos-modelos.json           # tabela de preços versionada (research R-21)
docker/
└── postgres/init/                 # cria bancos agente e rag, usuários, extensão vector no rag
docs/
├── adr/
└── desvios/
src/
├── Api.Agente/
│   ├── Api.Agente.Endpoints/      # host: Minimal APIs, A2A, auth, rate limit, OTel, Dockerfile
│   ├── Api.Agente.Application/    # casos de uso: validar, exportar CSV, conversar, importar NCM; orquestração das 9 etapas
│   ├── Api.Agente.Domain/         # motor de validação, leiaute, catálogo, achados (sem dependências externas)
│   │   └── Efd/Dados/             # leiaute-efd-icms-ipi-<COD_VER>.json, catalogo-regras-efd-icms-ipi.json
│   └── Api.Agente.Infrastructure/ # MongoDB, EF Core (agente), Semantic Kernel/OpenAI, cliente MCP, fila
├── Api.Rag/
│   ├── Api.Rag.Endpoints/         # host: REST admin, servidor MCP, Dockerfile
│   ├── Api.Rag.Application/       # ingestão, busca híbrida
│   ├── Api.Rag.Domain/            # documento, trecho, segmentação
│   └── Api.Rag.Infrastructure/    # EF Core + pgvector (rag), PdfPig, embeddings OpenAI
└── Shared/
    └── AgenteSpedFiscal.Contracts.Mcp/   # DTOs das ferramentas MCP (sem estado, sem dados)
tests/
├── Api.Agente.Domain.Tests/       # regras estruturais e de negócio (fixtures EFD)
├── Api.Agente.Application.Tests/  # orquestração, P-04, validação semântica do LLM (LLM falso)
├── Api.Agente.Integration.Tests/  # API, MongoDB, PostgreSQL, MCP (Testcontainers)
├── Api.Rag.Tests/                 # segmentação, deduplicação, busca filtrada por versão
├── Contracts.Tests/               # JSON Schemas, OpenAPI, Agent Card, catálogo
├── Architecture.Tests/            # direção de dependências (NetArchTest)
├── AgenteSpedFiscal.Evals/        # avaliação RAG/LLM sob demanda
└── fixtures/
    ├── efd/                       # arquivos sintéticos + gabaritos de achados
    └── base/                      # material interno de teste (classificação interno)
tools/
├── extrair-leiaute/               # gera o leiaute 020 a partir do Guia Prático
└── gerar-fixtures-efd/            # gera os arquivos EFD sintéticos e gabaritos
evals/                             # conjuntos de avaliação versionados e resultados
```

**Structure Decision**: dois serviços com camadas em projetos separados (exigência de separação
de P-08, verificável por compilação), uma biblioteca de contratos MCP compartilhada (permitida
por constitution 3.4) e testes separados por serviço e nível. Leiaute e catálogo ficam como
dados versionados dentro do domínio da Api.Agente, revisados em PR.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| 4 projetos por serviço (8 no total) | Direção de dependência de P-08 verificada pelo compilador; domínio sem SDKs | Um projeto com pastas depende só de teste de arquitetura e permite regressão silenciosa |
| Biblioteca `AgenteSpedFiscal.Contracts.Mcp` | Um único contrato MCP para cliente e servidor | DTOs duplicados divergem; JSON Schema sozinho não gera tipos |
| Execução assíncrona com fila em memória | Arquivos de 500 MB excedem timeouts de HTTP | Processamento síncrono; broker externo rejeitado por não haver necessidade de durabilidade (arquivo não é retido) |
| Busca híbrida (vetor + texto) | Perguntas citam códigos exatos (`C190`, `VL_OPR`) | Busca só vetorial recupera mal identificadores exatos (validado nos evals) |
