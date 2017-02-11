param(
    [Parameter(Mandatory=$false)][string]$srcRoot = ".",
    [Parameter(Mandatory=$true)][string]$keyFile,
    [Parameter(Mandatory=$true)][string]$publicKey,
    [Parameter(Mandatory=$false)][string]$publicToken
)

$snSignAssembly = "true"
$delaySignAssembly = "true"

Function GetXmlFile
{
    param(
        [Parameter(Mandatory=$true)][string]$inputFile
    )

    return [xml](Get-Content $inputFile)
}

Function MakeNodeWithInnerText
{
    param(
        [Parameter(Mandatory=$true)][xml]$inputXml,
        [Parameter(Mandatory=$true)][string]$newPropertyName,
        [Parameter(Mandatory=$true)][string]$newPropertyValue,
        [Parameter(Mandatory=$false)][string]$xmlns = ""
    )

    $newNode=$inputXml.CreateElement($newPropertyName, $xmlns)
    $newNode.set_InnerXML($newPropertyValue)

    return $newNode
}

Function EnableSNAndDelaySign
{
    param(
        [Parameter(Mandatory=$true)][xml]$projectXML
    )

    $xmlNameSpace = $projectXML.Project.xmlns
    $newPropertyGroup=$projectXML.CreateElement("PropertyGroup", $xmlNameSpace);
    
    $newKeyFileNode = MakeNodeWithInnerText $projectXML "AssemblyOriginatorKeyFile" $keyFile $xmlNameSpace
    $newSignAssemblyNode = MakeNodeWithInnerText $projectXML "SignAssembly" $snSignAssembly $xmlNameSpace
    $newDelaySignNode = MakeNodeWithInnerText $projectXML "DelaySign" $delaySignAssembly $xmlNameSpace

    $newPropertyGroup.AppendChild($newKeyFileNode)
    $newPropertyGroup.AppendChild($newSignAssemblyNode)
    $newPropertyGroup.AppendChild($newDelaySignNode)

    $projectXml.Project.AppendChild($newPropertyGroup)
}

Function InjectPublicKeyIntoAssemblyInfo
{
    param([string]$assemblyInfoFilePath)

    $assemblyInfo = Get-Content $assemblyInfoFilePath

    $injectedAssemblyInfo = $assemblyInfo -replace '(?<=\[assembly: InternalsVisibleTo\(")(.*)?(?="\)\])', "`${1},PublicKey=$publicKey"

    Set-Content $assemblyInfoFilePath $injectedAssemblyInfo
    Write-Host "Set public key in $assemblyInfoFilePath"
}

$xmls = "src\NuGet.Server\NuGet.Server.csproj",
"src\NuGet.Server.V2\NuGet.Server.V2.csproj",
"src\NuGet.Server.Core\NuGet.Server.Core.csproj",
"test\NuGet.Server.Tests\NuGet.Server.Tests.csproj",
"test\NuGet.Server.V2.Tests\NuGet.Server.V2.Tests.csproj",
"test\NuGet.Server.Core.Tests\NuGet.Server.Core.Tests.csproj"

$assemblyInfos = "src\NuGet.Server\Properties\AssemblyInfo.cs",
"src\NuGet.Server.V2\Properties\AssemblyInfo.cs",
"src\NuGet.Server.Core\Properties\AssemblyInfo.cs"

foreach ($relXmlPath in $xmls)
{
    $xmlFile = Join-Path -Path $srcRoot -ChildPath $relXmlPath -Resolve

    if (Test-Path $xmlFile)
    {
        $xml = GetXmlFile $xmlFile
        EnableSNAndDelaySign $xml

        echo $xmlFile

        $xml.Save($xmlFile)
    }
}

foreach ($assemblyInfoFile in $assemblyInfos)
{
    if (Test-Path $assemblyInfoFile)
    {
        InjectPublicKeyIntoAssemblyInfo $assemblyInfoFile
    }
}

if (-Not $publicToken) {
    exit 0
}

# Find latest version of Windows SDK installed
$sdkRegistry = Get-ItemProperty "hklm:\SOFTWARE\Microsoft\Microsoft SDKs\Windows"
$sdkInstallPath = $sdkRegistry.CurrentInstallFolder
$toolsPath = (Get-ChildItem "$sdkInstallPath\bin" | Select-Object -Last 1).FullName

# Run sn.exe with the public token
& "$toolsPath\sn.exe" "-Vr" "*,$publicToken"
& "$toolsPath\x64\sn.exe" "-Vr" "*,$publicToken"