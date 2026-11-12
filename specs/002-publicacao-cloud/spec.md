# Feature Specification: Publicação experimental em nuvem gratuita

**Feature**: `002-publicacao-cloud` | **Criado em**: 2026-11-10 | **Status**: rascunho

**Entrada**: publicar a Api.Agente e a Api.Rag (feature 001) no Vercel Hobby via Container Runtime,
com MongoDB no Atlas Free e PostgreSQL com pgvector no Neon Free, preservando arquitetura, rotas,
contratos REST, MCP e A2A, esquema dos bancos e segredos fora do Git. Decisão do usuário em
2026-11-10: **as duas APIs no Vercel**, com os limites da plataforma documentados como restrição da
demonstração.

## Escopo

- Ambiente de **demonstração/treinamento**, não de produção. Nenhum documento ou arquivo EFD real de
  cliente é enviado a este ambiente (LGPD e política da INFIS).
- Sem mudança de contratos, rotas ou esquema. Mudanças de código só onde a plataforma exige (FR-P06),
  com aprovação.
- O Docker Compose local continua sendo o ambiente de desenvolvimento.

## User Scenarios

### História 1 — SOFIA ou analista valida um arquivo pequeno pela URL pública (P1)

Um cliente autorizado envia um arquivo EFD de até ~4 MB à Api.Agente publicada e recebe o envelope
de validação, por REST ou A2A, com os achados determinísticos e, quando a base e o modelo respondem,
a análise da IA.

**Aceite**: (1) `POST /api/v1/validacoes` com token devolve `202` e a execução termina em
`concluida` ou `concluida_com_limitacoes` dentro de 300 s; (2) o Agent Card público anuncia as duas
skills com a URL `https` do Vercel; (3) sem token, `401`.

### História 2 — Consulta fundamentada na base (P1)

Uma conversa pergunta sobre o Guia 3.2.4 e recebe resposta com referências classificadas, vindas da
Api.Rag publicada por MCP com token de serviço.

**Aceite**: `fundamentadaEmFonteOficial = true` para pergunta coberta pelo Guia; a Api.Rag parada
leva a `fundamentada = false` com a limitação declarada (SC-004 da feature 001).

### História 3 — Operador publica e reverte com segurança (P2)

O operador cria os recursos gratuitos, configura variáveis, aplica migrações, publica e consegue
voltar à versão anterior sem perda de dados.

**Aceite**: roteiro executado com evidências (logs e URLs), rollback testado uma vez.

## Requisitos

- **FR-P01**: Cada API DEVE ser um projeto Vercel próprio, com variáveis próprias; nenhuma recebe a
  connection string da outra (constitution P-07).
- **FR-P02**: Segredos DEVEM ficar só nas variáveis do provedor (tipo *Sensitive*); nada no Git,
  Dockerfile, script ou log.
- **FR-P03**: O container DEVE escutar na `PORT` da plataforma, rodar como usuário não root e manter
  o Swagger fechado fora de `Development`.
- **FR-P04**: Os limites do Vercel Hobby DEVEM aparecer na documentação da demonstração e nas
  respostas: arquivos acima de ~4 MB recusados com `413` pela própria API; processamento limitado a
  300 s; arquivos grandes (SC-005 da 001) fora do alcance deste ambiente.
- **FR-P05**: O Guia 3.2.4 (≈ 6,5 MB, acima do limite) DEVE ser ingerido executando a Api.Rag
  localmente contra o Neon, não pela URL pública.
- **FR-P06**: Com várias instâncias, a recuperação na inicialização NÃO DEVE marcar como `falha`
  execuções em andamento em outra instância, nem reenfileirar ingestões que outra instância processa.
- **FR-P07**: Migrações DEVEM rodar como passo controlado antes do deploy, com backup (branch do
  Neon), nunca na inicialização em nuvem.
- **FR-P08**: O acesso de rede do Atlas NÃO DEVE ficar aberto a `0.0.0.0/0` sem aprovação explícita
  registrada em `docs/desvios/`, com os controles compensatórios.
- **FR-P09**: O uso do Vercel Hobby pela INFIS DEVE ser confirmado (plano para uso pessoal e não
  comercial) antes da publicação; se não for permitido, usar plano pago ou outra plataforma.

## Fora de escopo

Produção, domínio próprio, IP estático, provedor OIDC (ADR-0004), arquivos acima do limite da
plataforma e processamento acima de 300 s.

## Success Criteria

- **SC-P01**: As duas URLs públicas respondem `/health/live = 200` e `/health/ready = 200`.
- **SC-P02**: Validação de `valido-perfil-a.txt` pela URL pública termina em até 300 s, com o mesmo
  envelope do ambiente local.
- **SC-P03**: Nenhum segredo aparece no repositório, nos logs do Vercel ou nas respostas HTTP.
- **SC-P04**: Rollback para o deploy anterior em menos de 5 minutos, sem perda de dados.
