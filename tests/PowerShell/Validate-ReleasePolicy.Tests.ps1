$ErrorActionPreference = "Stop"
$policyScript = Join-Path $PSScriptRoot "..\..\scripts\Validate-ReleasePolicy.ps1"

$cases = @(
    [pscustomobject]@{
        Name = "main branch permits unsigned development builds"
        Reference = "refs/heads/main"
        ReferenceName = "main"
        AppVersion = "1.3.0"
        HasCertificate = $false
        HasPassword = $false
        ExpectedSuccess = $true
    },
    [pscustomobject]@{
        Name = "main branch accepts a complete signing configuration"
        Reference = "refs/heads/main"
        ReferenceName = "main"
        AppVersion = "1.3.0"
        HasCertificate = $true
        HasPassword = $true
        ExpectedSuccess = $true
    },
    [pscustomobject]@{
        Name = "matching stable tag requires signing configuration"
        Reference = "refs/tags/v1.3.0"
        ReferenceName = "v1.3.0"
        AppVersion = "1.3.0"
        HasCertificate = $true
        HasPassword = $true
        ExpectedSuccess = $true
    },
    [pscustomobject]@{
        Name = "stable tag rejects missing certificate"
        Reference = "refs/tags/v1.3.0"
        ReferenceName = "v1.3.0"
        AppVersion = "1.3.0"
        HasCertificate = $false
        HasPassword = $false
        ExpectedSuccess = $false
    },
    [pscustomobject]@{
        Name = "stable tag rejects a mismatched application version"
        Reference = "refs/tags/v1.3.1"
        ReferenceName = "v1.3.1"
        AppVersion = "1.3.0"
        HasCertificate = $true
        HasPassword = $true
        ExpectedSuccess = $false
    },
    [pscustomobject]@{
        Name = "partial signing configuration is rejected"
        Reference = "refs/heads/main"
        ReferenceName = "main"
        AppVersion = "1.3.0"
        HasCertificate = $true
        HasPassword = $false
        ExpectedSuccess = $false
    }
)

foreach ($case in $cases) {
    $actualSuccess = $true
    try {
        & $policyScript -Reference $case.Reference -ReferenceName $case.ReferenceName -AppVersion $case.AppVersion -HasCertificate $case.HasCertificate -HasPassword $case.HasPassword
    }
    catch {
        $actualSuccess = $false
    }

    if ($actualSuccess -ne $case.ExpectedSuccess) {
        throw "Release policy test failed: $($case.Name)"
    }
}

Write-Host "All release signing policy tests passed."
