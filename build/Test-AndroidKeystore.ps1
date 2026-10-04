# Checks the Android release keystore, password, alias and key password before the build, so a signing failure says
# which one is wrong instead of jarsigner's bare "exited with code 1". Never prints a secret; only lengths and hints.
param(
    [Parameter(Mandatory)][string]$Keystore,
    [string]$StorePassword,
    [string]$Alias,
    [string]$KeyPassword
)

$ErrorActionPreference = 'Stop'

function Describe([string]$name, [string]$value) {
    if ([string]::IsNullOrEmpty($value)) { return "$name is EMPTY" }
    $hints = @()
    if ($value -ne $value.Trim()) { $hints += 'has leading/trailing whitespace' }
    if ($value.Contains('$')) { $hints += "contains '$' (in GitLab, untick 'Expand variable reference' for it)" }
    $suffix = if ($hints) { ' - ' + ($hints -join ', ') } else { '' }
    "$name is set ($($value.Length) characters)$suffix"
}

$bytes = [IO.File]::ReadAllBytes($Keystore)
$format = if ($bytes.Length -ge 4 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xED -and $bytes[2] -eq 0xFE -and $bytes[3] -eq 0xED) { 'JKS' }
          elseif ($bytes.Length -gt 0 -and $bytes[0] -eq 0x30) { 'PKCS12' }
          else { 'unrecognised (is ANDROID_KEYSTORE_BASE64 the base64 of the .keystore/.jks file?)' }
Write-Host "Keystore: $($bytes.Length) bytes, format $format"
Write-Host (Describe 'ANDROID_KEYSTORE_PASSWORD' $StorePassword)
Write-Host (Describe 'ANDROID_KEY_ALIAS' $Alias)
Write-Host (Describe 'Key password (ANDROID_KEY_PASSWORD, else the keystore password)' $KeyPassword)

# keytool ships with the JDK the Android build uses
$keytool = @(
    if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin\keytool.exe' }
    (Get-Command keytool.exe -ErrorAction SilentlyContinue).Source
    Get-ChildItem 'C:\Program Files\Microsoft\jdk-*\bin\keytool.exe', 'C:\Program Files\Android\openjdk\*\bin\keytool.exe',
        'C:\Program Files\Eclipse Adoptium\*\bin\keytool.exe' -ErrorAction SilentlyContinue | ForEach-Object FullName
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $keytool) { Write-Warning 'keytool.exe not found; skipping the keystore check.'; return }

# 1. Store password: listing the keystore fails with "password was incorrect" otherwise
$list = & $keytool -list -keystore $Keystore -storepass $StorePassword 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "The keystore could not be opened with ANDROID_KEYSTORE_PASSWORD: $($list.Trim())"
}
# Entry lines look like "mykey, Oct 4, 2026, PrivateKeyEntry," (the date has commas of its own)
$aliases = [regex]::Matches($list, '(?m)^(.+?), .*, (PrivateKeyEntry|trustedCertEntry|SecretKeyEntry),') | ForEach-Object { $_.Groups[1].Value }
Write-Host "Keystore opened; entries: $($aliases -join ', ')"

# 2. Alias: ask for it by name (keytool fails with "Alias <x> does not exist")
& $keytool -list -keystore $Keystore -storepass $StorePassword -alias $Alias 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "ANDROID_KEY_ALIAS doesn't match an entry in the keystore. Its entries are: $($aliases -join ', ')"
}

# 3. Key password. PKCS12 keystores (keytool's default) have one password for the store and its keys; keytool ignores a
#    different key password, but jarsigner fails with it, so call that out. JKS keys can have their own password.
if ($format -eq 'PKCS12') {
    if ($KeyPassword -cne $StorePassword) {
        throw "This is a PKCS12 keystore, which uses the keystore password for its key, but the key password differs. " +
              "Remove ANDROID_KEY_PASSWORD (or make it the same as ANDROID_KEYSTORE_PASSWORD)."
    }
    Write-Host "Keystore, alias and key password all check out."
    return
}
# JKS: reading the private key (to make a signing request) fails if its password is wrong
$tmp = [IO.Path]::GetTempFileName()
try {
    $req = & $keytool -certreq -keystore $Keystore -storepass $StorePassword -alias $Alias -keypass $KeyPassword -file $tmp 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "The key '$Alias' could not be read with its key password (ANDROID_KEY_PASSWORD, or the keystore password when that is unset): $($req.Trim())"
    }
}
finally {
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}
Write-Host "Keystore, alias and key password all check out."
