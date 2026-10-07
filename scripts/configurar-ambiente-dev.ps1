<#
.SYNOPSIS
  Prepara o ambiente de desenvolvimento do SmartStock AI nesta máquina.

.DESCRIPTION
  1. Pede a senha do usuário "postgres" (digitada por você; não é gravada em lugar nenhum).
  2. Cria no PostgreSQL local o usuário "smartstock_app" (senha aleatória) e os bancos
     "smartstock" (desenvolvimento) e "smartstock_tests" (testes automatizados).
  3. Grava a connection string e uma chave JWT aleatória nos "user-secrets" do .NET
     (fora da pasta do projeto, em %APPDATA%\Microsoft\UserSecrets).
  4. Opcional: grava a senha da conta de e-mail smartstock@ (só necessária para envio real).

  Pode ser executado de novo: recria a senha do smartstock_app e a chave JWT.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File ".\scripts\configurar-ambiente-dev.ps1"
#>

$ErrorActionPreference = 'Stop'

$apiProject = Join-Path $PSScriptRoot '..\backend\src\SmartStock.Api'
$psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$appRole = 'smartstock_app'
$databases = @('smartstock', 'smartstock_tests')

function New-RandomSecret([int]$length) {
    $chars = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $bytes = New-Object byte[] $length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

function ConvertTo-PlainText([securestring]$secure) {
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

function Invoke-Psql([string]$sql, [string]$database = 'postgres') {
    $output = & $psql -h localhost -U postgres -d $database -v ON_ERROR_STOP=1 -tA -c $sql
    if ($LASTEXITCODE -ne 0) { throw "Falha ao executar comando no PostgreSQL." }
    return $output
}

if (-not (Test-Path $psql)) { throw "psql não encontrado em $psql. O PostgreSQL 17 está instalado?" }

Write-Host ''
Write-Host '=== SmartStock AI: configuração do ambiente de desenvolvimento ===' -ForegroundColor Cyan
Write-Host ''

$env:PGPASSWORD = ConvertTo-PlainText (Read-Host 'Senha do usuário "postgres" (definida na instalação)' -AsSecureString)

try {
    Invoke-Psql 'SELECT 1' | Out-Null
    Write-Host '[ok] Conectado ao PostgreSQL.' -ForegroundColor Green

    $appPassword = New-RandomSecret 32
    $exists = Invoke-Psql "SELECT 1 FROM pg_roles WHERE rolname = '$appRole'"
    if ($exists -eq '1') {
        Invoke-Psql "ALTER ROLE $appRole WITH LOGIN CREATEDB PASSWORD '$appPassword'" | Out-Null
    } else {
        Invoke-Psql "CREATE ROLE $appRole WITH LOGIN CREATEDB PASSWORD '$appPassword'" | Out-Null
    }
    Write-Host "[ok] Usuário do banco '$appRole' pronto." -ForegroundColor Green

    foreach ($db in $databases) {
        if ((Invoke-Psql "SELECT 1 FROM pg_database WHERE datname = '$db'") -ne '1') {
            Invoke-Psql "CREATE DATABASE $db OWNER $appRole ENCODING 'UTF8'" | Out-Null
        } else {
            Invoke-Psql "ALTER DATABASE $db OWNER TO $appRole" | Out-Null
        }
        Write-Host "[ok] Banco '$db' pronto." -ForegroundColor Green
    }
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}

$connectionString = "Host=localhost;Port=5432;Database=smartstock;Username=$appRole;Password=$appPassword"
dotnet user-secrets set 'ConnectionStrings:Default' $connectionString --project $apiProject | Out-Null
dotnet user-secrets set 'Jwt:SigningKey' (New-RandomSecret 64) --project $apiProject | Out-Null
Write-Host '[ok] Connection string e chave JWT gravadas nos user-secrets.' -ForegroundColor Green

Write-Host ''
$answer = Read-Host 'Deseja gravar agora a senha do e-mail smartstock@example.com? (s/N)'
if ($answer -match '^[sS]') {
    $mailPassword = ConvertTo-PlainText (Read-Host 'Senha do e-mail smartstock@' -AsSecureString)
    dotnet user-secrets set 'Email:Password' $mailPassword --project $apiProject | Out-Null
    Write-Host '[ok] Senha do e-mail gravada nos user-secrets.' -ForegroundColor Green
}

Write-Host ''
Write-Host 'Ambiente configurado. Nenhuma senha foi gravada dentro da pasta do projeto.' -ForegroundColor Cyan
