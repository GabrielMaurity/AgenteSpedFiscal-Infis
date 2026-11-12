<#
.SYNOPSIS
  Publica uma das APIs no Vercel (Container Runtime, beta), cada uma no seu próprio projeto Vercel.

.DESCRIPTION
  Projetos separados mantêm as variáveis de ambiente isoladas: cada API recebe só a própria connection
  string (constitution P-07). O Vercel usa o Dockerfile.vercel da raiz; o script copia o da API escolhida,
  publica e remove a cópia. Não grava segredos: token e IDs vêm de variáveis de ambiente.

  Requer: Vercel CLI (npm i -g vercel), VERCEL_TOKEN, VERCEL_ORG_ID e o ID do projeto da API em
  VERCEL_PROJECT_ID_AGENTE ou VERCEL_PROJECT_ID_RAG. As variáveis da aplicação ficam no painel do projeto
  (ver deploy/vercel/variaveis-*.md).

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

foreach ($variavel in 'VERCEL_TOKEN', 'VERCEL_ORG_ID') {
    if (-not [Environment]::GetEnvironmentVariable($variavel)) { throw "Defina $variavel." }
}
$projeto = [Environment]::GetEnvironmentVariable("VERCEL_PROJECT_ID_$($Api.ToUpperInvariant())")
if (-not $projeto) { throw "Defina VERCEL_PROJECT_ID_$($Api.ToUpperInvariant())." }
if (Test-Path $destino) { throw "Já existe Dockerfile.vercel na raiz; remova-o antes de publicar." }

Copy-Item $origem $destino
try {
    $env:VERCEL_PROJECT_ID = $projeto
    $argumentos = @('deploy', '--yes', '--token', $env:VERCEL_TOKEN)
    if ($Producao) { $argumentos += '--prod' }
    Push-Location $raiz
    try { & vercel @argumentos; if ($LASTEXITCODE -ne 0) { throw "vercel deploy terminou com código $LASTEXITCODE." } }
    finally { Pop-Location }
}
finally {
    Remove-Item $destino -ErrorAction SilentlyContinue
    Remove-Item Env:VERCEL_PROJECT_ID -ErrorAction SilentlyContinue
}
