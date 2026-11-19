[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Reference,

    [Parameter(Mandatory = $true)]
    [string]$ReferenceName,

    [Parameter(Mandatory = $true)]
    [string]$AppVersion,

    [Parameter(Mandatory = $true)]
    [bool]$HasCertificate,

    [Parameter(Mandatory = $true)]
    [bool]$HasPassword
)

$ErrorActionPreference = "Stop"

if ($HasCertificate -xor $HasPassword) {
    throw "Both WINDOWS_SIGNING_CERTIFICATE_BASE64 and WINDOWS_SIGNING_CERTIFICATE_PASSWORD must be configured together."
}

$isVersionedRelease = $Reference -like "refs/tags/*"
if ($isVersionedRelease -and $ReferenceName -ne "v$AppVersion") {
    throw "Release tag '$ReferenceName' must match the application version tag 'v$AppVersion'."
}

if ($isVersionedRelease -and -not $HasCertificate) {
    throw "Stable, versioned releases are blocked until a trusted Authenticode certificate is configured."
}
