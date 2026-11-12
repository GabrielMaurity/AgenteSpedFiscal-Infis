# Desvio 0001: Acesso de rede do MongoDB Atlas aberto a qualquer IP (0.0.0.0/0)

- **Regra afetada**: constitution 5.1 (dados protegidos em trânsito e em repouso, com acesso mínimo)
  e a regra 4 da skill `sofia-devops-free-cloud` (não usar `0.0.0.0/0` como configuração definitiva
  do Atlas). Feature 002, FR-P08 e tarefa T012.
- **Justificativa**: a Api.Agente publicada no Vercel Hobby precisa acessar o cluster `Cluster0` do
  MongoDB Atlas Free. As Vercel Functions do plano Hobby não têm IP de saída fixo (IP estático é
  recurso pago do Vercel), então não há allowlist restrita possível nesse plano. A alternativa
  restrita exige plano pago ou outra hospedagem com egress fixo.
- **Impacto**: o cluster aceita tentativas de conexão de qualquer endereço da internet. Controles
  compensatórios obrigatórios enquanto o desvio vigorar:
  - TLS em todas as conexões (`mongodb+srv`);
  - usuário de banco com senha longa gerada pelo Atlas e **apenas** `readWrite` no banco `agente`,
    sem papéis administrativos (verificado por teste que tenta gravar fora do banco `agente`);
  - nenhum dado real de cliente neste ambiente: uso exclusivo de demonstração e treinamento, com
    arquivos fictícios (`tests/fixtures/efd/`);
  - credencial só nas variáveis do Vercel (*Sensitive*) e no `.env` local não versionado; rotação
    imediata se houver suspeita de vazamento;
  - acompanhamento dos alertas de acesso do Atlas.
- **Responsável**: responsável pelo repositório AgenteSpedFiscal-Infis.
- **Aprovação**: aprovado pelo responsável em 2026-11-12, na sessão de publicação da feature 002.
- **Data**: 2026-11-12
- **Prazo de revisão** (temporário): até a publicação deixar o Vercel Hobby (plano pago com IP
  estático ou outra hospedagem com egress fixo) ou em 2027-02-12, o que vier primeiro. Na revisão, a
  regra `0.0.0.0/0` DEVE ser removida ou o desvio reavaliado de forma explícita, sem renovação tácita
  (constitution 8.3).
