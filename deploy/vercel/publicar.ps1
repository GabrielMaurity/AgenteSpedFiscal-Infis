<#
.SYNOPSIS
  Publica uma das APIs no Vercel (Container Runtime, beta), cada uma no seu próprio projeto Vercel.

.DESCRIPTION
  Projetos separados mantêm as variáveis de ambiente isoladas: cada API recebe só a própria connection
  string (constitution P-07). O Vercel usa o Dockerfile.vercel da raiz; o script copia o da API escolhida,
  publica e remove a cópia. Não grava segredos.

  Autenticação: a sessão do `vercel login` ou, em CI, a variável VERCEL_TOKEN. Os IDs do time e dos
  projetos (não são segredos) têm padrão abaixo e podem ser trocados por VERCEL_ORG_ID e
  VERCEL_PROJECT_ID_AGENTE / VERCEL_PROJECT_ID_RAG. As variáveis da aplicação ficam no projeto
  (ver deploy/vercel/variaveis.md).

.EXAMPLE
  ./deploy/vercel/publicar.ps1 -Api agente            # preview
  ./deploy/vercel/publicar.ps1 -Api rag -Producao     # produção
#>
param(
    [Parameter(Mandatory)] [ValidateSet('agente', 'rag')] [string] $Api,
    [switch] $Producao
)

$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$origem = Join-Path $PSScriptRoot "api-$Api\Dockerfile.vercel"
$destino = Join-Path $raiz 'Dockerfile.vercel'

# Time CSC e projetos criados em 2026-11-12 (feature 002, T015).
$padroes = @{
    ORG    = 'team_yT5T81wFidgowV7KFyUbxeyi'
    RAG    = 'prj_T0asZ2gAnfmFphWK6nswbKFgoP1P'
    AGENTE = 'prj_9cBzm04r97yk1y8l0dNvZQ1whFmB'
}
$org = if ($env:VERCEL_ORG_ID) { $env:VERCEL_ORG_ID } else { $padroes.ORG }
$chave = $Api.ToUpperInvariant()
$projeto = [Environment]::GetEnvironmentVariable("VERCEL_PROJECT_ID_$chave")
if (-not $projeto) { $projeto = $padroes[$chave] }
if (Test-Path $destino) { throw "Já existe Dockerfile.vercel na raiz; remova-o antes de publicar." }

Copy-Item $origem $destino
try {
    $env:VERCEL_ORG_ID = $org
    $env:VERCEL_PROJECT_ID = $projeto
    $argumentos = @('deploy', '--yes')
    if ($env:VERCEL_TOKEN) { $argumentos += @('--token', $env:VERCEL_TOKEN) }
    if ($Producao) { $argumentos += '--prod' }
    Push-Location $raiz
    try { & vercel @argumentos; if ($LASTEXITCODE -ne 0) { throw "vercel deploy terminou com código $LASTEXITCODE." } }
    finally { Pop-Location }
}
finally {
    Remove-Item $destino -ErrorAction SilentlyContinue
    Remove-Item Env:VERCEL_PROJECT_ID -ErrorAction SilentlyContinue
}
