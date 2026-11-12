# Implementation Plan: Publicação experimental em nuvem gratuita

**Feature**: `002-publicacao-cloud` | **Spec**: [spec.md](spec.md) | **Data**: 2026-11-10

## Resumo

Dois projetos no Vercel Hobby (Container Runtime, beta), um por API, construídos a partir de
`deploy/vercel/api-*/Dockerfile.vercel`. MongoDB no Atlas Free e PostgreSQL no Neon Free, com dois
bancos (`agente`, `rag`) e a extensão `vector` só no `rag`. Sem mudança de contrato; uma correção de
código para o modelo de várias instâncias (FR-P06).

## Limites conferidos em 2026-11-10 (documentação oficial do Vercel)

| Limite | Valor no Hobby | Fonte |
|---|---|---|
| Container Runtime | beta, em todos os planos | vercel.com/docs/functions/runtimes/container |
| Corpo da requisição/resposta | 4,5 MB (resposta em streaming não conta) | vercel.com/docs/functions/limitations |
| Duração máxima | 300 s | idem |
| Memória | 2 GB / 1 vCPU | idem |
| Ociosidade | instância desligada após 5 min sem tráfego (SIGTERM + 30 s) | runtimes/container |
| Porta | 80, ou `PORT` definida no projeto | runtimes/container |
| IP estático | recurso pago | KB "Does Vercel support Docker deployments" |
| Uso do plano | pessoal e não comercial | vercel.com/docs/plans/hobby (confirmar) |

Atlas Free e Neon Free: limites de armazenamento, conexões, compute e transferência variam; conferir
nas páginas oficiais no dia da publicação (links no [quickstart.md](quickstart.md)).

## Matriz de compatibilidade

| Item | Situação | Tratamento |
|---|---|---|
| .NET 10 em container multi-stage, usuário não root | OK | `Dockerfile.vercel` com `aspnet:10.0` (tem shell para ler `PORT`) |
| Upload de EFD até 500 MB | Bloqueio | Limite da demo em 4 MB (`Limites__TamanhoMaximoArquivoBytes`) |
| Ingestão do Guia (≈ 6,5 MB) | Bloqueio | Ingestão local contra o Neon (FR-P05) |
| Fila em memória + `BackgroundService` | Adaptação | Execuções curtas; a instância vive ≥ 5 min após a última requisição; risco documentado |
| Recuperação na inicialização (`FalharEmAndamentoAsync`, `RetomarPendentesAsync`) | Adaptação (feita) | T008: limiar `Recuperacao:*` com `Recuperacao__InstanciaUnica=false` no Vercel |
| Arquivo grande (SC-005, 15 min) | Bloqueio | Fora de escopo neste ambiente |
| Skill A2A de validação (espera dentro da requisição) | Adaptação | Arquivos pequenos, abaixo de 300 s |
| MCP sem estado, busca, conversas | OK | — |
| Limitadores em memória | Adaptação | Valem por instância; limite efetivo mais frouxo, documentado |
| Diretório temporário de upload | Adaptação | `/tmp/agente-uploads`, não durável; arquivo já é descartado ao fim (FR-082) |
| OTLP / Aspire Dashboard | Adaptação | Sem `OTEL_EXPORTER_OTLP_ENDPOINT`; logs no stdout |
| Migrações na inicialização | Adaptação | `false` na nuvem; passo controlado (FR-P07) |
| MongoDB com TTL de 90 dias | OK | Atlas Free aceita índice TTL |
| Postgres com dois bancos e `vector` | Adaptação | `deploy/neon/01-bancos-e-roles.sql` |
| Acesso de rede ao Atlas | Risco | IP dinâmico do Vercel; abertura exige aprovação (FR-P08) |
| Uso do Hobby pela INFIS | Risco/Bloqueio | Confirmar termos (FR-P09) |
| Testado localmente (2026-11-10) | OK | Imagens compiladas; com `PORT=9090`: `/health/live` 200, `/health/ready` 503 sem bancos, Swagger 401, endpoints sem token 401, `uid=1654` |
| Retestado com US3, T008, modo sem autenticação e `proximidade` (2026-11-10) | OK | Build Release das duas imagens; com `PORT=9090` e JWT de teste: health 200/503, Swagger e OpenAPI 401, REST sem token 401, Agent Card 401 sem token e 200 com token (duas skills, URL `https`), `/mcp` 401 sem token e 200 com token de serviço; `Autenticacao__Desabilitada=true` em Production impede o início com mensagem clara |
| Modo sem autenticação (R-37 da 001) | OK | Só vale em `Development`; nas imagens (`Production`) a API não inicia com ele ligado |

## Estrutura

```text
deploy/
├── vercel/
│   ├── api-agente/Dockerfile.vercel
│   ├── api-rag/Dockerfile.vercel
│   ├── publicar.ps1          # copia o Dockerfile da API para a raiz, publica e remove a cópia
│   └── variaveis.md          # nomes das variáveis por projeto (sem valores)
└── neon/01-bancos-e-roles.sql
specs/002-publicacao-cloud/   # spec, plano, tarefas, roteiro e critérios de aceite
```

O Vercel só detecta `Dockerfile.vercel` na raiz do projeto. Como as duas APIs compartilham a raiz do
repositório (contexto de build com `Directory.Packages.props`, `src/` e `data/`), o script copia o
arquivo da API escolhida para a raiz no momento do deploy. Projetos com "services" foram descartados:
as variáveis de ambiente são compartilhadas entre os serviços, o que fere a P-07.

## Constitution Check

- P-07 (isolamento entre serviços): projetos separados e bancos/usuários separados. ✔
- P-19 (segredos): só no provedor; `variaveis.md` lista nomes. ✔
- P-04/FR-084 (limitações declaradas): limites da plataforma aparecem como `413`, `falha_tecnica` ou
  limitação. ✔
- Desvios: abertura de rede do Atlas e uso do Hobby, se aprovados, entram em `docs/desvios/`.

## Riscos

- Container Runtime em beta: comportamento de processamento após a resposta precisa ser medido (T011).
- Cold start da instância e do Neon (suspensão por inatividade) na primeira requisição.
- Custos da OpenAI não estão incluídos em "gratuito".
