[CmdletBinding()]
param(
    [string]$RepoOwner = "maxhenkentech",
    [string]$RepoName = "MSPP-IroncladCLM",
    [string]$Ref = "main",
    [string]$ConnectorName = "Ironclad CLM",
    [string]$StateRoot = (Join-Path ([Environment]::GetFolderPath("UserProfile")) ".ironcladclm"),
    [string]$ConnectorSecret = "dummy"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:PythonCommand = $null
$script:PaconnConfigDirectory = $null
$script:ExtractedBundleRoot = $null

function Write-Section {
    param([string]$Message)
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "[Step] $Message" -ForegroundColor Yellow
}

function Write-Detail {
    param([string]$Message)
    Write-Host "  - $Message" -ForegroundColor Gray
}

function Test-CommandExists {
    param([string]$Name)
    return $null -ne (Get-Command -Name $Name -ErrorAction SilentlyContinue)
}

function Get-PreferredPythonCommand {
    foreach ($candidate in @("python3", "python")) {
        if (Test-CommandExists -Name $candidate) {
            return $candidate
        }
    }

    throw "Python 3 is required to install or run paconn. Install Python 3 and rerun this script."
}

function Invoke-PythonCommand {
    param(
        [string[]]$Arguments,
        [switch]$Interactive
    )

    if (-not $script:PythonCommand) {
        throw "Python command was not initialized."
    }

    if ($Interactive) {
        & $script:PythonCommand @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Python command failed with exit code $LASTEXITCODE."
        }

        return @()
    }

    $output = & $script:PythonCommand @Arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        foreach ($line in @($output)) {
            Write-Host "    $line" -ForegroundColor DarkGray
        }

        throw "Python command failed with exit code $exitCode."
    }

    return @($output)
}

function Ensure-ConsoleUiModule {
    Write-Step "Checking the interactive console UI module used for the action and environment pickers."

    if (Get-Command -Name Out-ConsoleGridView -ErrorAction SilentlyContinue) {
        Write-Detail "Out-ConsoleGridView is already available."
        return
    }

    Write-Detail "Installing Microsoft.PowerShell.ConsoleGuiTools for the picker UI."
    Install-Module -Name Microsoft.PowerShell.ConsoleGuiTools -Scope CurrentUser -Force -AllowClobber
    Import-Module Microsoft.PowerShell.ConsoleGuiTools -ErrorAction Stop

    if (-not (Get-Command -Name Out-ConsoleGridView -ErrorAction SilentlyContinue)) {
        throw "Failed to load Out-ConsoleGridView after installing Microsoft.PowerShell.ConsoleGuiTools."
    }
}

function Ensure-PaconnInstalled {
    Write-Step "Checking for Python and paconn."

    $script:PythonCommand = Get-PreferredPythonCommand
    Write-Detail "Using Python command '$script:PythonCommand'."

    $paconnAvailable = $false
    try {
        Invoke-PythonCommand -Arguments @("-m", "paconn", "--version") | Out-Null
        $paconnAvailable = $true
    }
    catch {
        $paconnAvailable = $false
    }

    if (-not $paconnAvailable) {
        Write-Detail "paconn is not installed. Installing it for the current user."

        try {
            Invoke-PythonCommand -Arguments @("-m", "pip", "--version") | Out-Null
        }
        catch {
            Write-Detail "pip was not available. Bootstrapping pip with ensurepip."
            Invoke-PythonCommand -Arguments @("-m", "ensurepip", "--upgrade")
        }

        Invoke-PythonCommand -Arguments @("-m", "pip", "install", "--user", "--upgrade", "paconn")
    }

    $versionOutput = Invoke-PythonCommand -Arguments @("-m", "paconn", "--version")
    Write-Detail ("paconn is ready. Version: {0}" -f (($versionOutput -join " ").Trim()))
}

function Get-PaconnConfigDirectory {
    if ($script:PaconnConfigDirectory) {
        return $script:PaconnConfigDirectory
    }

    $configDir = (Invoke-PythonCommand -Arguments @("-c", "from paconn.common.util import get_config_dir; print(get_config_dir())"))[-1].Trim()
    if ([string]::IsNullOrWhiteSpace($configDir)) {
        throw "Could not determine the paconn config directory."
    }

    $script:PaconnConfigDirectory = $configDir
    return $script:PaconnConfigDirectory
}

function Invoke-PaconnLogin {
    Write-Step "Running paconn login."
    Write-Detail "If you are not already signed in, paconn will show a device-code login prompt."
    Invoke-PythonCommand -Arguments @("-m", "paconn", "login") -Interactive
}

function Get-PaconnAccessToken {
    $configDir = Get-PaconnConfigDirectory
    $tokenFile = Join-Path $configDir "accessTokens.json"

    if (-not (Test-Path -Path $tokenFile)) {
        throw "The paconn access token file was not found at '$tokenFile'."
    }

    $token = Get-Content -Path $tokenFile -Raw | ConvertFrom-Json
    if (-not $token.access_token) {
        throw "The paconn token file does not contain an access token."
    }

    return $token
}

function Invoke-PowerAppsRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [Parameter(Mandatory = $true)]
        [psobject]$Token
    )

    return Invoke-RestMethod -Method Get -Uri $Uri -Headers @{
        Authorization = "$($Token.token_type) $($Token.access_token)"
        "x-ms-origin" = "paconn-cli"
        Accept = "application/json"
    }
}

function Get-ObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        $Object,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    if ($Object -is [System.Collections.IDictionary]) {
        return $Object[$Name]
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($property) {
        return $property.Value
    }

    return $null
}

function ConvertTo-EnvironmentRecord {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        $Environment,
        [Parameter(Mandatory = $true)]
        [string]$Source
    )

    $properties = Get-ObjectPropertyValue -Object $Environment -Name "properties"
    $environmentId = Get-ObjectPropertyValue -Object $Environment -Name "name"
    if ([string]::IsNullOrWhiteSpace($environmentId)) {
        $environmentId = Get-ObjectPropertyValue -Object $properties -Name "environmentId"
    }
    if ([string]::IsNullOrWhiteSpace($environmentId)) {
        $environmentId = Get-ObjectPropertyValue -Object $Environment -Name "id"
    }

    $displayName = Get-ObjectPropertyValue -Object $properties -Name "displayName"
    if ([string]::IsNullOrWhiteSpace($displayName)) {
        $displayName = Get-ObjectPropertyValue -Object $Environment -Name "displayName"
    }
    if ([string]::IsNullOrWhiteSpace($displayName)) {
        $displayName = $environmentId
    }

    $environmentType = Get-ObjectPropertyValue -Object $properties -Name "environmentType"
    if ([string]::IsNullOrWhiteSpace($environmentType)) {
        $environmentType = Get-ObjectPropertyValue -Object $properties -Name "environmentSku"
    }
    if ([string]::IsNullOrWhiteSpace($environmentType)) {
        $environmentType = Get-ObjectPropertyValue -Object $Environment -Name "type"
    }

    $location = Get-ObjectPropertyValue -Object $properties -Name "azureRegionHint"
    if ([string]::IsNullOrWhiteSpace($location)) {
        $location = Get-ObjectPropertyValue -Object $properties -Name "location"
    }
    if ([string]::IsNullOrWhiteSpace($location)) {
        $location = Get-ObjectPropertyValue -Object $Environment -Name "location"
    }

    if ([string]::IsNullOrWhiteSpace($environmentId)) {
        return $null
    }

    return [pscustomobject]@{
        DisplayName   = $displayName
        EnvironmentId = $environmentId
        Type          = $environmentType
        Location      = $location
        Source        = $Source
        Raw           = $Environment
    }
}

function Get-AccessibleEnvironments {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Token
    )

    Write-Step "Retrieving the environments you can deploy to."

    $requestTargets = @(
        @{
            Name = "Power Apps RP"
            Uri  = "https://api.powerapps.com/providers/Microsoft.PowerApps/environments?api-version=2016-11-01"
        },
        @{
            Name = "Business App Platform user scope"
            Uri  = "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/user/environments?api-version=2020-10-01"
        }
    )

    $lastError = $null
    foreach ($target in $requestTargets) {
        try {
            Write-Detail ("Trying environment discovery via {0}." -f $target.Name)
            $response = Invoke-PowerAppsRequest -Uri $target.Uri -Token $Token
            $items = @(Get-ObjectPropertyValue -Object $response -Name "value")
            if ($items.Count -eq 0 -and $response -is [System.Collections.IEnumerable] -and -not ($response -is [string])) {
                $items = @($response)
            }

            $environments = @(
                foreach ($item in $items) {
                    $record = ConvertTo-EnvironmentRecord -Environment $item -Source $target.Name
                    if ($null -ne $record) {
                        $record
                    }
                }
            ) | Sort-Object DisplayName -Unique

            if ($environments.Count -gt 0) {
                Write-Detail ("Found {0} environments." -f $environments.Count)
                return $environments
            }
        }
        catch {
            $lastError = $_
            Write-Detail ("Environment discovery via {0} failed: {1}" -f $target.Name, $_.Exception.Message)
        }
    }

    throw "Unable to retrieve Power Platform environments after paconn login. $($lastError.Exception.Message)"
}

function Select-Action {
    Ensure-ConsoleUiModule

    Write-Step "Asking whether you want to install a new connector or update an existing one."
    $actions = @(
        [pscustomobject]@{
            Action      = "Install"
            Description = "Create a new Ironclad CLM custom connector in one or more environments."
        },
        [pscustomobject]@{
            Action      = "Update"
            Description = "Update an existing Ironclad CLM custom connector with the latest GitHub payload."
        }
    )

    $selection = $actions | Out-ConsoleGridView -Title "Select the connector action to run" -OutputMode Single
    if ($null -eq $selection) {
        throw "No action was selected. The installer cannot continue."
    }

    return $selection.Action
}

function Select-Environments {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Environments
    )

    Ensure-ConsoleUiModule

    Write-Step "Presenting the environment picker. Use Space to select multiple environments, then press Enter."
    $selection = $Environments |
        Select-Object DisplayName, EnvironmentId, Type, Location, Source |
        Out-ConsoleGridView -Title "Select the environments for the Ironclad CLM connector" -OutputMode Multiple

    if ($null -eq $selection -or @($selection).Count -eq 0) {
        throw "No environments were selected. The installer cannot continue."
    }

    return @($selection)
}

function Select-ConnectorRegistration {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentId,
        [Parameter(Mandatory = $true)]
        [object[]]$Connectors
    )

    Ensure-ConsoleUiModule

    Write-Step "More than one matching connector was found in environment '$EnvironmentId'. Asking you to pick the one to update."
    $selection = $Connectors |
        Select-Object DisplayName, ConnectorId, CreatedBy, EnvironmentId |
        Out-ConsoleGridView -Title "Select the connector to update in $EnvironmentId" -OutputMode Single

    if ($null -eq $selection) {
        throw "No connector was selected for environment '$EnvironmentId'."
    }

    return $selection
}

function New-TemporaryWorkingDirectory {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("ironcladclm-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return $root
}

function Download-ConnectorBundle {
    Write-Step "Downloading the latest connector payload from GitHub."

    $workingRoot = New-TemporaryWorkingDirectory
    $archivePath = Join-Path $workingRoot "connector.zip"
    $extractPath = Join-Path $workingRoot "bundle"
    $zipUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/zipball/$Ref"

    Invoke-WebRequest -Uri $zipUrl -OutFile $archivePath -Headers @{
        "User-Agent" = "IroncladCLM-Installer"
        Accept       = "application/vnd.github+json"
    }

    Expand-Archive -Path $archivePath -DestinationPath $extractPath -Force

    $bundleRoot = Get-ChildItem -Path $extractPath -Directory | Select-Object -First 1
    if ($null -eq $bundleRoot) {
        throw "The downloaded GitHub archive did not contain a top-level folder."
    }

    $connectorRoot = Join-Path $bundleRoot.FullName "connector"
    foreach ($requiredPath in @(
        (Join-Path $connectorRoot "apiDefinition.swagger.json"),
        (Join-Path $connectorRoot "apiProperties.json"),
        (Join-Path $connectorRoot "script.csx"),
        (Join-Path $connectorRoot "icon.png")
    )) {
        if (-not (Test-Path -Path $requiredPath)) {
            throw "The downloaded GitHub payload is missing '$requiredPath'."
        }
    }

    $script:ExtractedBundleRoot = $workingRoot

    Write-Detail ("Using connector assets from {0}" -f $connectorRoot)
    return [pscustomobject]@{
        WorkingRoot   = $workingRoot
        ConnectorRoot = $connectorRoot
        ApiDefinition = Join-Path $connectorRoot "apiDefinition.swagger.json"
        ApiProperties = Join-Path $connectorRoot "apiProperties.json"
        Script        = Join-Path $connectorRoot "script.csx"
        Icon          = Join-Path $connectorRoot "icon.png"
    }
}

function Get-DeploymentDirectory {
    $deploymentDirectory = Join-Path $StateRoot "deployments"
    if (-not (Test-Path -Path $deploymentDirectory)) {
        New-Item -ItemType Directory -Path $deploymentDirectory -Force | Out-Null
    }

    return $deploymentDirectory
}

function Get-SettingsFilePath {
    param([Parameter(Mandatory = $true)][string]$EnvironmentId)
    return (Join-Path (Get-DeploymentDirectory) ("{0}_settings.json" -f $EnvironmentId))
}

function Write-SettingsFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SettingsFilePath,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentId,
        [Parameter(Mandatory = $true)]
        [psobject]$Bundle,
        [string]$ConnectorId
    )

    $settings = [ordered]@{
        environment          = $EnvironmentId
        apiProperties        = $Bundle.ApiProperties
        apiDefinition        = $Bundle.ApiDefinition
        icon                 = $Bundle.Icon
        powerAppsUrl         = "https://api.powerapps.com"
        powerAppsApiVersion  = "2016-11-01"
        script               = $Bundle.Script
    }

    if (-not [string]::IsNullOrWhiteSpace($ConnectorId)) {
        $settings.connectorId = $ConnectorId
    }

    $settings | ConvertTo-Json -Depth 8 | Set-Content -Path $SettingsFilePath -Encoding utf8
}

function Get-ConnectorRecordsFromResponse {
    param([Parameter(Mandatory = $true)]$Response)

    $items = @(Get-ObjectPropertyValue -Object $Response -Name "value")
    if ($items.Count -eq 0) {
        $items = @(Get-ObjectPropertyValue -Object $Response -Name "apis")
    }

    return @($items)
}

function Get-ConnectorRegistrations {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Token,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentId
    )

    $filter = [uri]::EscapeDataString("environment eq '$EnvironmentId'")
    $uri = "https://api.powerapps.com/providers/Microsoft.PowerApps/apis?api-version=2016-11-01&`$filter=$filter"
    $response = Invoke-PowerAppsRequest -Uri $uri -Token $Token

    return @(
        foreach ($item in (Get-ConnectorRecordsFromResponse -Response $response)) {
            $properties = Get-ObjectPropertyValue -Object $item -Name "properties"
            [pscustomobject]@{
                DisplayName   = (Get-ObjectPropertyValue -Object $properties -Name "displayName")
                ConnectorId   = (Get-ObjectPropertyValue -Object $item -Name "name")
                CreatedBy     = (Get-ObjectPropertyValue -Object (Get-ObjectPropertyValue -Object $properties -Name "createdBy") -Name "displayName")
                EnvironmentId = $EnvironmentId
                Raw           = $item
            }
        }
    )
}

function Resolve-ConnectorIdForUpdate {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Token,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentId,
        [Parameter(Mandatory = $true)]
        [psobject]$Bundle
    )

    $settingsFilePath = Get-SettingsFilePath -EnvironmentId $EnvironmentId
    if (Test-Path -Path $settingsFilePath) {
        $savedSettings = Get-Content -Path $settingsFilePath -Raw | ConvertFrom-Json
        if ($savedSettings.connectorId) {
            Write-Detail ("Using saved settings file '{0}'." -f $settingsFilePath)
            Write-SettingsFile -SettingsFilePath $settingsFilePath -EnvironmentId $EnvironmentId -Bundle $Bundle -ConnectorId $savedSettings.connectorId
            return [pscustomobject]@{
                ConnectorId     = $savedSettings.connectorId
                SettingsFile    = $settingsFilePath
                ConnectorRecord = $null
            }
        }
    }

    Write-Detail ("No saved settings were found for environment '{0}'. Looking for existing connectors in that environment." -f $EnvironmentId)
    $connectors = Get-ConnectorRegistrations -Token $Token -EnvironmentId $EnvironmentId
    $matches = @(
        $connectors | Where-Object {
            $_.DisplayName -eq $ConnectorName -or $_.ConnectorId -like "shared_ironclad*"
        }
    )

    if ($matches.Count -eq 0) {
        throw "No existing '$ConnectorName' connector was found in environment '$EnvironmentId', and no saved settings file exists."
    }

    $selectedConnector = if ($matches.Count -eq 1) {
        $matches[0]
    }
    else {
        Select-ConnectorRegistration -EnvironmentId $EnvironmentId -Connectors $matches
    }

    Write-SettingsFile -SettingsFilePath $settingsFilePath -EnvironmentId $EnvironmentId -Bundle $Bundle -ConnectorId $selectedConnector.ConnectorId
    return [pscustomobject]@{
        ConnectorId     = $selectedConnector.ConnectorId
        SettingsFile    = $settingsFilePath
        ConnectorRecord = $selectedConnector
    }
}

function Invoke-PaconnDeployment {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Install", "Update")]
        [string]$Mode,
        [Parameter(Mandatory = $true)]
        [string]$SettingsFilePath
    )

    $verb = if ($Mode -eq "Install") { "create" } else { "update" }
    Write-Step ("Running paconn {0}." -f $verb)

    $arguments = @("-m", "paconn", $verb, "-s", $SettingsFilePath)
    if ($Mode -eq "Install") {
        $arguments += @("--secret", $ConnectorSecret, "--overwrite-settings")
    }

    $output = Invoke-PythonCommand -Arguments $arguments
    foreach ($line in $output) {
        Write-Host "    $line" -ForegroundColor DarkGray
    }
}

function Find-RedirectUrls {
    param(
        [Parameter(Mandatory = $true)]
        [AllowNull()]
        $Node
    )

    $urls = New-Object System.Collections.Generic.List[string]
    $pattern = [regex]'https://[^"''\s]+/redirect(?:/[^"''\s]+)?'

    function Search-Node {
        param($Value)

        if ($null -eq $Value) {
            return
        }

        if ($Value -is [string]) {
            foreach ($match in $pattern.Matches($Value)) {
                if (-not $urls.Contains($match.Value)) {
                    $urls.Add($match.Value)
                }
            }
            return
        }

        if ($Value -is [System.Collections.IDictionary]) {
            foreach ($entry in $Value.GetEnumerator()) {
                Search-Node -Value $entry.Value
            }
            return
        }

        if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
            foreach ($item in $Value) {
                Search-Node -Value $item
            }
            return
        }

        foreach ($property in $Value.PSObject.Properties) {
            Search-Node -Value $property.Value
        }
    }

    Search-Node -Value $Node
    return @($urls)
}

function Get-ConnectorRedirectUrls {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Token,
        [Parameter(Mandatory = $true)]
        [string]$EnvironmentId,
        [Parameter(Mandatory = $true)]
        [string]$ConnectorId
    )

    Write-Step ("Retrieving the connector registration so the generated redirect URL can be reported for environment '{0}'." -f $EnvironmentId)
    $filter = [uri]::EscapeDataString("environment eq '$EnvironmentId'")
    $uri = "https://api.powerapps.com/providers/Microsoft.PowerApps/apis/$ConnectorId?api-version=2016-11-01&`$filter=$filter"
    $response = Invoke-PowerAppsRequest -Uri $uri -Token $Token

    $redirectUrls = Find-RedirectUrls -Node $response
    return @($redirectUrls)
}

function Show-Intro {
    Write-Section "Ironclad CLM custom connector installer"
    Write-Host "This script downloads the latest connector payload from GitHub, checks paconn, signs you in, lets you pick environments, and then installs or updates the Ironclad CLM connector." -ForegroundColor White
    Write-Host "For each completed deployment it saves an environment-specific settings file and then tries to read the deployed connector back from Power Platform so it can print any generated redirect URLs." -ForegroundColor White
}

function Remove-TemporaryBundle {
    if ($script:ExtractedBundleRoot -and (Test-Path -Path $script:ExtractedBundleRoot)) {
        Remove-Item -Path $script:ExtractedBundleRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    Show-Intro
    Ensure-ConsoleUiModule
    Ensure-PaconnInstalled
    Invoke-PaconnLogin

    $token = Get-PaconnAccessToken
    $mode = Select-Action
    $environments = Get-AccessibleEnvironments -Token $token
    $selectedEnvironments = Select-Environments -Environments $environments
    $bundle = Download-ConnectorBundle

    $results = @()
    foreach ($environment in $selectedEnvironments) {
        $environmentId = $environment.EnvironmentId
        Write-Section ("Processing environment {0}" -f $environmentId)
        Write-Detail ("Environment name: {0}" -f $environment.DisplayName)

        $settingsFilePath = Get-SettingsFilePath -EnvironmentId $environmentId
        $connectorId = $null
        try {
            if ($mode -eq "Install") {
                Write-SettingsFile -SettingsFilePath $settingsFilePath -EnvironmentId $environmentId -Bundle $bundle
            }
            else {
                $resolvedConnector = Resolve-ConnectorIdForUpdate -Token $token -EnvironmentId $environmentId -Bundle $bundle
                $settingsFilePath = $resolvedConnector.SettingsFile
                $connectorId = $resolvedConnector.ConnectorId
            }

            Invoke-PaconnDeployment -Mode $mode -SettingsFilePath $settingsFilePath

            $savedSettings = Get-Content -Path $settingsFilePath -Raw | ConvertFrom-Json
            $connectorId = $savedSettings.connectorId
            if ([string]::IsNullOrWhiteSpace($connectorId)) {
                throw "paconn completed but the connector ID was not written to '$settingsFilePath'."
            }

            $redirectUrls = @()
            try {
                $redirectUrls = Get-ConnectorRedirectUrls -Token $token -EnvironmentId $environmentId -ConnectorId $connectorId
            }
            catch {
                Write-Detail ("The connector was deployed but the redirect URL lookup failed: {0}" -f $_.Exception.Message)
            }

            $results += [pscustomobject]@{
                EnvironmentName = $environment.DisplayName
                EnvironmentId   = $environmentId
                Mode            = $mode
                ConnectorId     = $connectorId
                SettingsFile    = $settingsFilePath
                RedirectUrls    = @($redirectUrls)
                Status          = "Succeeded"
            }

            Write-Detail ("Deployment finished. Settings file: {0}" -f $settingsFilePath)
            if ($redirectUrls.Count -gt 0) {
                foreach ($redirectUrl in $redirectUrls) {
                    Write-Host "  Redirect URL: $redirectUrl" -ForegroundColor Green
                }
            }
            else {
                Write-Host "  Redirect URL: No redirect URL was discovered automatically in the connector registration." -ForegroundColor Yellow
            }
        }
        catch {
            $results += [pscustomobject]@{
                EnvironmentName = $environment.DisplayName
                EnvironmentId   = $environmentId
                Mode            = $mode
                ConnectorId     = $connectorId
                SettingsFile    = if (Test-Path -Path $settingsFilePath) { $settingsFilePath } else { $null }
                RedirectUrls    = @()
                Status          = "Failed"
                Error           = $_.Exception.Message
            }

            Write-Host ("  Deployment failed for environment {0}: {1}" -f $environmentId, $_.Exception.Message) -ForegroundColor Red
        }
    }

    Write-Section "Deployment summary"
    foreach ($result in $results) {
        Write-Host ("[{0}] {1} ({2})" -f $result.Status, $result.EnvironmentName, $result.EnvironmentId) -ForegroundColor $(if ($result.Status -eq "Succeeded") { "Green" } else { "Red" })
        if ($result.ConnectorId) {
            Write-Detail ("Connector ID: {0}" -f $result.ConnectorId)
        }
        if ($result.SettingsFile) {
            Write-Detail ("Settings file: {0}" -f $result.SettingsFile)
        }
        if ($result.RedirectUrls.Count -gt 0) {
            foreach ($redirectUrl in $result.RedirectUrls) {
                Write-Detail ("Redirect URL: {0}" -f $redirectUrl)
            }
        }
        elseif ($result.Status -eq "Succeeded") {
            Write-Detail "Redirect URL: not discovered automatically from the connector registration."
        }
        if ($result.PSObject.Properties.Name -contains "Error") {
            Write-Detail ("Error: {0}" -f $result.Error)
        }
    }

    if ($results.Where({ $_.Status -eq "Failed" }).Count -gt 0) {
        throw "One or more environments failed. Review the summary above."
    }
}
catch {
    Write-Host ""
    Write-Host ("Installer failed: {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 1
}
finally {
    Remove-TemporaryBundle
}
