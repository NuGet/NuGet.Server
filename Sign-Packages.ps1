[CmdletBinding()]
param (
    [string]$Version,
    [string]$BuildQueueUrl,
    [string]$BuildId,
    [string]$DropPath,
    [string]$UserName,
    [string]$Password
)

# Copy artifacts to share.
$dropSubdirectory = Join-Path "$DropPath" "$Version"

New-Item -path $dropSubdirectory -type Directory
Copy-Item -path "artifacts\packages\*.nupkg" -Destination $dropSubdirectory -verbose -force

# Queue TeamCity build to sign packages
$xml = [xml]@"
<build>
    <buildType id="$BuildId"/>
    <properties>
        <property name="NuGetDropLocation" value="$dropSubdirectory"/>
    </properties>
</build>
"@

$securePassword = ConvertTo-SecureString "$Password" -AsPlainText -Force
$TeamCityCredentials = New-Object System.Management.Automation.PSCredential ($UserName, $securePassword)

$out = Invoke-WebRequest -Uri "$BuildQueueUrl" -Credential $TeamCityCredentials -Method POST -Body $xml.OuterXml -ContentType 'application/xml' -UseBasicParsing -Verbose

if (-not $out) {
    throw "Failed to push packages to server."
}

if ($out.StatusCode -ne 200) {
    Write-Output $out.RawContent
    throw "Request failed with StatusCode=$($out.StatusCode)"
}

$rsp = [xml]$out.Content
Write-Output $rsp.OuterXml