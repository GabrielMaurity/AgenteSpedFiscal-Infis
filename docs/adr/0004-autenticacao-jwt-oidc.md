# ADR-0004: Autenticação JWT/OIDC

**Status**: Proposto (provedor OIDC de produção em aberto)
**Data**: 2026-11-05

## Contexto

REST, MCP e A2A exigem autenticação (constitution 5.1) e isolamento por organização (FR-080).

## Decisão

- JWT Bearer em todos os endpoints, exceto `/health/*`.
- Claims: `sub`, `org_id` e papéis `analista`, `admin_base`, `agente_externo`.
- Entre serviços: token com audiência `api-rag`.
- Desenvolvimento: `dotnet user-jwts`, com configuração em `Authentication:Schemes:Bearer`.

## Alternativas consideradas

- **API keys**: sem identidade de usuário nem papéis.
- **Cookies**: não atendem agentes.

## Consequências

- O código depende só de JWT padrão, então o provedor pode ser escolhido depois.
- **Em aberto**: provedor OIDC de produção e emissão do token de serviço (client credentials).
