<#
.SYNOPSIS
    Builds .env from a template, filling in values from Azure Key Vault.

.DESCRIPTION
    Containers can't use `az login`, so the secrets are resolved here, with the
    caller's login, and handed to Docker Compose through the git-ignored .env.

    A setting takes its value from the vault when a secret of the matching name
    exists; otherwise its template value is kept. Names map as: __ to --, then
    _ to - (ConnectionStrings__Default becomes ConnectionStrings--Default).

    Azure-Postgres-User and Azure-Postgres-Password, if the vault has them,
    replace the login inside ConnectionStrings--Default. That secret still
    supplies the server, database and SSL settings.

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

$databaseUserSecret = 'Azure-Postgres-User'
$databasePasswordSecret = 'Azure-Postgres-Password'

function Get-VaultSecret([string] $Name) {
    $value = az keyvault secret show --vault-name $VaultName --name $Name --query value -o tsv
    if ($LASTEXITCODE -ne 0) { throw "Can't read secret '$Name'." }
    return $value
}

function Set-DatabaseLogin([string] $ConnectionString, [string] $User, [string] $Password) {
    # The builder quotes a password containing ; or quotes. PowerShell hides its
    # ConnectionString property, so the accessors are called directly.
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($ConnectionString)
    foreach ($keyword in 'Username', 'User Name', 'User Id', 'UserId', 'UID', 'Password', 'PWD', 'PSW') {
        [void] $builder.Remove($keyword)
    }
    $builder['Username'] = $User
    $builder['Password'] = $Password
    return $builder.get_ConnectionString()
}

$root = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $root $Source
$outputPath = Join-Path $root $Output

$vaultNames = az keyvault secret list --vault-name $VaultName --query '[].name' -o tsv
if ($LASTEXITCODE -ne 0) { throw "Can't list secrets in '$VaultName'. Run az login and check your access." }

$hasDatabaseUser = $vaultNames -contains $databaseUserSecret
$hasDatabasePassword = $vaultNames -contains $databasePasswordSecret
if ($hasDatabaseUser -ne $hasDatabasePassword) {
    throw "The vault has only one of '$databaseUserSecret' and '$databasePasswordSecret'. Add both or neither."
}

$fromVault = 0
$lines = foreach ($line in [IO.File]::ReadAllLines($sourcePath)) {
    if ($line -notmatch '^([A-Za-z_][A-Za-z0-9_]*)=') { $line; continue }

    $key = $Matches[1]
    $secretName = $key.Replace('__', '--').Replace('_', '-')
    if ($vaultNames -notcontains $secretName) { $line; continue }

    $fromVault++
    $replacesLogin = $key -eq 'ConnectionStrings__Default' -and $hasDatabaseUser
    if ($DryRun) {
        Write-Host "vault: $key <- $secretName"
        if ($replacesLogin) { Write-Host "vault: login in $key <- $databaseUserSecret, $databasePasswordSecret" }
        $line
        continue
    }

    $value = Get-VaultSecret $secretName
    if ($replacesLogin) {
        $value = Set-DatabaseLogin $value (Get-VaultSecret $databaseUserSecret) (Get-VaultSecret $databasePasswordSecret)
    }

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
