#!/bin/bash
# Cria um banco e um usuário por serviço (constitution P-07, ADR-0007).
# É .sh, e não .sql, para ler as senhas do ambiente em vez de versioná-las.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v agente_senha="$POSTGRES_AGENTE_PASSWORD" \
  -v rag_senha="$POSTGRES_RAG_PASSWORD" <<'SQL'
CREATE USER agente WITH PASSWORD :'agente_senha';
CREATE USER rag WITH PASSWORD :'rag_senha';
CREATE DATABASE agente OWNER agente;
CREATE DATABASE rag OWNER rag;
REVOKE ALL ON DATABASE agente FROM PUBLIC;
REVOKE ALL ON DATABASE rag FROM PUBLIC;
SQL

# A extensão vector existe somente no banco rag.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname rag \
  -c "CREATE EXTENSION IF NOT EXISTS vector;"
