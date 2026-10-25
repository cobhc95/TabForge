param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$Subject
)

$cert = Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    # Local dev builds stay unsigned; when release signing is required (TABFORGE_REQUIRE_SIGNING=1) a missing certificate is an error.
    if ($env:TABFORGE_REQUIRE_SIGNING -eq '1') {
        Write-Host "SIGNING FAILED: TABFORGE_REQUIRE_SIGNING=1 but no certificate '$Subject' with a private key is in CurrentUser\My."
        exit 1
    }
    Write-Host "Signing skipped: no certificate '$Subject' with a private key in CurrentUser\My, so the exe is unsigned."
    exit 0
}

$result = Set-AuthenticodeSignature -FilePath $ExePath -Certificate $cert -HashAlgorithm SHA256
if ($result.SignerCertificate -eq $null) {
    Write-Host "Signing failed for ${ExePath}: $($result.StatusMessage)"
    exit 1
}

Write-Host "Signed $ExePath with '$Subject' (SHA256)."
exit 0
