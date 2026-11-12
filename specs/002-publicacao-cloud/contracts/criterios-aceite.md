# Critérios de aceite da publicação

Cada item precisa de evidência real (URL, status HTTP, trecho de log sem segredo). Nada é dado como
publicado sem ela.

| # | Verificação | Esperado | Requisito |
|---|---|---|---|
| A01 | `GET https://<api-agente>/health/live` e `/health/ready` | `200` e `200` | SC-P01 |
| A02 | `GET https://<api-rag>/health/live` e `/health/ready` | `200` e `200` | SC-P01 |
| A03 | `GET https://<api-agente>/swagger/index.html` | diferente de `200` (Swagger fechado em produção) | FR-P03 |
| A04 | `POST /api/v1/validacoes` sem token | `401` | FR-080 (001) |
| A05 | `GET /.well-known/agent-card.json` com token `agente_externo` | `200`, duas skills, URL `https://<api-agente>/a2a` | FR-081 (001) |
| A06 | Validar `valido-perfil-a.txt` por REST | status final em até 300 s; envelope válido no `envelope-validacao.schema.json` | SC-P02 |
| A07 | A mesma validação por A2A | mesmos achados do REST | P-20 (001) |
| A08 | Arquivo de 5 MB | `413` com `application/problem+json` | FR-P04 |
| A09 | Pergunta "O valor de IBS entra no VL_DOC do C100 em 2026?" com `periodo=2026-03` | `fundamentadaEmFonteOficial = true`, referência `oficial` | História 2 |
| A10 | Api.Rag com `McpRag__TokenServico` inválido na Api.Agente | validação `concluida_com_limitacoes` com `etapa-5` `falha_tecnica` | SC-004 (001) |
| A11 | `MongoDb`: documento da execução tem `expiraEm` = criação + 90 dias | sim | FR-082 (001) |
| A12 | Busca por segredos: `git grep` no repositório e Runtime Logs do Vercel | nenhuma connection string, chave ou token | SC-P03 |
| A13 | Instant Rollback e restauração da branch do Neon | versão anterior no ar em < 5 min, dados preservados | SC-P04 |
