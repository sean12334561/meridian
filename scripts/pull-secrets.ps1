<#
.SYNOPSIS
    Builds .env from a template, filling in values from Azure Key Vault.

.DESCRIPTION
    Containers can't use `az login`, so the secrets are resolved here, with the
    caller's login, and handed to Docker Compose through the git-ignored .env.

    A setting takes its value from the vault when a secret of the matching name
    exists; otherwise its template value is kept. Names map as: __ to --, then
    _ to - (ConnectionStrings__Default becomes ConnectionStrings--Default).

.EXAMPLE
    ./scripts/pull-secrets.ps1 -VaultName dotnetcandidate9kv
.EXAMPLE
    ./scripts/pull-secrets.ps1 -VaultName dotnetcandidate9kv -DryRun
#>
param(
    [Parameter(Mandatory)] [string] $VaultName,
    [string] $Source = '.env.example',
    [string] $Output = '.env',
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $root $Source
$outputPath = Join-Path $root $Output

$vaultNames = az keyvault secret list --vault-name $VaultName --query '[].name' -o tsv
if ($LASTEXITCODE -ne 0) { throw "Can't list secrets in '$VaultName'. Run az login and check your access." }

$fromVault = 0
$lines = foreach ($line in [IO.File]::ReadAllLines($sourcePath)) {
    if ($line -notmatch '^([A-Za-z_][A-Za-z0-9_]*)=') { $line; continue }

    $key = $Matches[1]
    $secretName = $key.Replace('__', '--').Replace('_', '-')
    if ($vaultNames -notcontains $secretName) { $line; continue }

    $fromVault++
    if ($DryRun) { Write-Host "vault: $key <- $secretName"; $line; continue }

    $value = az keyvault secret show --vault-name $VaultName --name $secretName --query value -o tsv
    if ($LASTEXITCODE -ne 0) { throw "Can't read secret '$secretName'." }

    # Single quotes stop Compose expanding $ in passwords
    "$key='$($value -replace "'", "\'")'"
}

if ($DryRun) {
    Write-Host "$fromVault setting(s) would come from '$VaultName'. Nothing written."
    return
}

# No BOM: it would become part of the first key
[IO.File]::WriteAllText($outputPath, ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Wrote $Output with $fromVault setting(s) from '$VaultName'."
