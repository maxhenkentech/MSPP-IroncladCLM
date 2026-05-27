[CmdletBinding()]
param(
    [string]$RepoOwner = "maxhenkentech",
    [string]$RepoName = "MSPP-IroncladCLM",
    [string]$Ref = "main",
    [string]$ConnectorSecret = "dummy",
    [switch]$DiagnosticMode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:PythonCommand = $null
$script:ExtractedBundleRoot = $null
$script:VenvPath = Join-Path $env:LOCALAPPDATA "ironcladclm-paconn-venv"

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
        if (-not (Test-CommandExists -Name $candidate)) { continue }
        try {
            $ver = & $candidate --version 2>&1
            if ($LASTEXITCODE -eq 0 -and ($ver -join "") -match "Python 3\.\d+") {
                return $candidate
            }
        }
        catch {}
    }
    return $null
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

function Ensure-PaconnInstalled {
    Write-Step "Setting up Python environment and paconn."

    # Refresh PATH first so recently-installed Python is visible before we check
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

    $systemPython = Get-PreferredPythonCommand
    if (-not $systemPython) {
        Write-Detail "Python 3 not found. Attempting to install via winget."
        if (-not (Test-CommandExists -Name "winget")) {
            throw "Python 3 is not installed and winget is not available. Install Python 3 from https://www.python.org/downloads/ and rerun this script."
        }
        & winget install Python.Python.3.12 --silent --accept-package-agreements --accept-source-agreements
        # Refresh PATH regardless of winget exit code ("no upgrade found" returns non-zero)
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
        $systemPython = Get-PreferredPythonCommand
        if (-not $systemPython) {
            throw "Python 3 could not be found after install. Try opening a new terminal and rerunning this script."
        }
    }
    Write-Detail "Using system Python: $systemPython"

    $venvPython = Join-Path $script:VenvPath "Scripts\python.exe"
    if (-not (Test-Path -Path $venvPython)) {
        Write-Detail "Creating virtual environment at $($script:VenvPath)."
        & $systemPython -m venv $script:VenvPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create Python virtual environment."
        }
    }
    $script:PythonCommand = $venvPython

    $paconnAvailable = $false
    try {
        $null = & $script:PythonCommand -m paconn --version 2>&1
        $paconnAvailable = ($LASTEXITCODE -eq 0)
    }
    catch { $paconnAvailable = $false }

    if (-not $paconnAvailable) {
        Write-Detail "Installing paconn in virtual environment."
        Write-Detail "Note: pip may warn about missing dev-only dependencies (flake8, pylint, pytest) — these are not needed at runtime and can be safely ignored."
        # Run pip without 2>&1 so pip output goes straight to the terminal.
        # 2>&1 in PS5.1 wraps stderr lines as ErrorRecord objects which trigger
        # ErrorActionPreference=Stop even during variable assignment.
        # Install paconn without its declared dev dependencies (pylint~=2.0.0 has broken
        # Requires-Python metadata that newer pip rejects). Runtime deps installed separately.
        & $script:PythonCommand -m pip install paconn --no-deps
        if ($LASTEXITCODE -ne 0) { throw "Failed to install paconn (pip exit $LASTEXITCODE)." }

        $runtimeDeps = @(
            "docutils", "future", "requests", "adal", "msrestazure", "virtualenv",
            "knack~=0.5.1", "azure-storage-blob<12.0,>=2.1"
        )
        & $script:PythonCommand -m pip install @runtimeDeps
        if ($LASTEXITCODE -ne 0) { throw "Failed to install paconn runtime dependencies (pip exit $LASTEXITCODE)." }
    }

    Write-Detail "Verifying paconn."
    & $script:PythonCommand -m paconn --version
    if ($LASTEXITCODE -ne 0) { throw "paconn is installed but failed to run." }
}

function Invoke-PaconnLogin {
    Write-Step "Running paconn login."
    Write-Detail "If you are not already signed in, paconn will show a device-code login prompt."
    Invoke-PythonCommand -Arguments @("-m", "paconn", "login") -Interactive
}

function Prompt-Choice {
    param([string]$Message)
    Write-Host -NoNewline $Message
    return (Read-Host).Trim()
}

function Select-Action {
    Write-Step "Asking whether you want to install a new connector or update an existing one."
    Write-Host ""
    Write-Host "  [1] Install  - Create a new Ironclad CLM custom connector." -ForegroundColor White
    Write-Host "  [2] Update   - Update an existing Ironclad CLM custom connector." -ForegroundColor White
    Write-Host ""

    $choice = ""
    while ($choice -ne "1" -and $choice -ne "2") {
        $choice = Prompt-Choice "Enter action number [1/2]: "
        if ([string]::IsNullOrWhiteSpace($choice)) { $choice = "1" }
    }

    if ($choice -eq "1") { return "Install" }
    return "Update"
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

function Invoke-PaconnDeployment {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Install", "Update")]
        [string]$Mode,
        [Parameter(Mandatory = $true)]
        [psobject]$Bundle
    )

    $verb = if ($Mode -eq "Install") { "create" } else { "update" }
    Write-Step ("Running paconn {0}." -f $verb)
    Write-Detail "paconn will prompt you to select the target environment interactively."
    if ($Mode -eq "Update") {
        Write-Detail "You will also be prompted to select which connector to update."
    }
    Write-Detail "Pushing connector assets - this can take a minute."

    $secret = if ($Mode -eq "Install") { $ConnectorSecret } else { "placeholder" }

    # Mirror what manage-ironclad-connector.py does on Windows: subprocess.run with no
    # output capture so the process inherits the real console handles.
    # Start-Process -NoNewWindow is the PowerShell equivalent.
    # WorkingDirectory is the connector folder so paconn writes settings.json there.
    $paconnArgs = @(
        "-m", "paconn", $verb,
        "--api-prop", $Bundle.ApiProperties,
        "--api-def",  $Bundle.ApiDefinition,
        "--script",   $Bundle.Script,
        "--icon",     $Bundle.Icon,
        "--secret",   $secret
    )
    if ($DiagnosticMode) { $paconnArgs += "--debug" }

    # Quote path arguments that contain spaces for the Start-Process argument string.
    $argString = ($paconnArgs | ForEach-Object {
        if ($_ -match ' ') { '"{0}"' -f $_ } else { $_ }
    }) -join ' '

    $proc = Start-Process -FilePath $script:PythonCommand `
                          -ArgumentList $argString `
                          -WorkingDirectory $Bundle.ConnectorRoot `
                          -NoNewWindow -Wait -PassThru

    if ($proc.ExitCode -ne 0) {
        throw "paconn $verb failed with exit code $($proc.ExitCode)."
    }

    if ($Mode -eq "Update") {
        return [pscustomobject]@{ ConnectorId = $null; EnvironmentId = $null }
    }

    # Try to read connector ID from settings.json that paconn writes after create.
    $connectorId  = $null
    $environmentId = $null
    $settingsFile = Join-Path $Bundle.ConnectorRoot "settings.json"
    if (Test-Path $settingsFile) {
        try {
            $s = Get-Content $settingsFile -Raw | ConvertFrom-Json
            $connectorId   = if ($s.PSObject.Properties['connectorId'])  { $s.connectorId }  else { $s.connector_id }
            $environmentId = if ($s.PSObject.Properties['environment'])  { $s.environment }  else { $null }
        }
        catch { }
    }

    # Fallback: paconn printed the connector ID above but we couldn't capture it.
    # Ask the user to paste it (same approach as manage-ironclad-connector.py on Windows).
    if ([string]::IsNullOrWhiteSpace($connectorId)) {
        Write-Host ""
        Write-Host "  The connector ID could not be read automatically." -ForegroundColor Yellow
        Write-Host "  It was printed by paconn above (e.g. 'shared_ironclad-clm-xxxx created successfully.')." -ForegroundColor White
        $connectorId = (Read-Host "  Paste the connector ID (or press Enter to skip)").Trim()
        if ([string]::IsNullOrWhiteSpace($connectorId)) { $connectorId = $null }
    }

    return [pscustomobject]@{ ConnectorId = $connectorId; EnvironmentId = $environmentId }
}

function Get-ConnectorRedirectUrls {
    param([Parameter(Mandatory = $true)][string]$ConnectorId)

    Write-Step "Deriving the generated redirect URL from the deployed connector ID."
    $redirectId = if ($ConnectorId.ToLower().StartsWith("shared_")) { $ConnectorId.Substring(7) } else { $ConnectorId }
    return @("https://global.consent.azure-apim.net/redirect/$redirectId")
}

function Show-Intro {
    Write-Section "Ironclad CLM custom connector installer"
    Write-Host "This script downloads the latest connector payload from GitHub, checks paconn, signs you in, and then installs or updates the Ironclad CLM connector." -ForegroundColor White
    Write-Host "paconn will prompt you to select the target environment (and connector for updates). The redirect URL is derived automatically from the connector ID." -ForegroundColor White
}

function Remove-TemporaryBundle {
    if ($script:ExtractedBundleRoot -and (Test-Path -Path $script:ExtractedBundleRoot)) {
        Remove-Item -Path $script:ExtractedBundleRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    Show-Intro
    Ensure-PaconnInstalled
    Invoke-PaconnLogin

    $mode = Select-Action
    $bundle = Download-ConnectorBundle

    $deployment = Invoke-PaconnDeployment -Mode $mode -Bundle $bundle
    $connectorId = $deployment.ConnectorId
    $environmentId = $deployment.EnvironmentId

    Write-Section "Deployment Summary"
    if (-not [string]::IsNullOrWhiteSpace($connectorId)) {
        Write-Host "  Connector ID: $connectorId" -ForegroundColor Green
        if (-not [string]::IsNullOrWhiteSpace($environmentId)) {
            Write-Detail "Environment:  $environmentId"
        }

        try {
            $redirectUrls = Get-ConnectorRedirectUrls -ConnectorId $connectorId
            foreach ($redirectUrl in $redirectUrls) {
                Write-Host "  Redirect URL: $redirectUrl" -ForegroundColor Green
            }
        }
        catch {
            Write-Detail ("Could not derive redirect URL: {0}" -f $_.Exception.Message)
        }

        if ($mode -eq "Install") {
            Write-Section "Next Steps - Configure Ironclad OAuth"
            Write-Host "  The connector is deployed. Before users can authenticate, register the Redirect URL" -ForegroundColor White
            Write-Host "  above in your Ironclad application so that OAuth can complete successfully." -ForegroundColor White
            Write-Step "Register the Redirect URL in Ironclad"
            Write-Detail "Log in to your Ironclad account."
            Write-Detail "Navigate to Company Settings -> API tab."
            Write-Detail "Open your existing application or click 'Create new app'."
            Write-Detail "Under 'Redirect URIs', paste the Redirect URL shown above."
            Write-Detail "Ensure Grant Type 'Authorization Code' is selected."
            Write-Detail "Add ALL of the following scopes - the connector will not work if any are missing:"
            Write-Detail "  workflows, workflow-schemas, users, companies, approvals, metadata, webhooks"
            Write-Detail "Save the application and securely note your Client ID and Client Secret."
        }
    }
    elseif ($mode -eq "Update") {
        Write-Host ""
        Write-Host "  Connector updated successfully." -ForegroundColor Green
        Write-Detail "The redirect URL is unchanged after an update."
    }
    else {
        Write-Host ""
        Write-Warning "Connector was created but the connector ID could not be read back. The redirect URL cannot be derived automatically."
    }
}
catch {
    $msg = $_.Exception.Message
    Write-Host ""
    Write-Host ("[FAIL]  Installer failed: {0}" -f $msg) -ForegroundColor Red
    if ($msg -match "exit code 1") {
        Write-Host "  [!]  If this looks like a transient API error, wait a moment and run the script again." -ForegroundColor Yellow
        Write-Host "       If authentication has expired, the next run will prompt you to log in again." -ForegroundColor Yellow
    }
    elseif ($msg -match "exit code 2") {
        Write-Host "  ^  paconn rejected a command-line argument. This may indicate a version mismatch - try upgrading paconn." -ForegroundColor Yellow
    }
    elseif ($msg -match "unassigned function app") {
        Write-Host "  [!]  'Unable to find an unassigned function app in region' is a known, intermittently occurring" -ForegroundColor Yellow
        Write-Host "       Microsoft Power Platform infrastructure issue. This error is often transient." -ForegroundColor Yellow
        Write-Host "       Wait a few minutes and run the installer again — it frequently resolves on its own." -ForegroundColor Yellow
        Write-Host "       If the error still occurs after 24 hours, raise a Microsoft support ticket and ask" -ForegroundColor Yellow
        Write-Host "       them to provision a function app slot in your region." -ForegroundColor Yellow
    }
    exit 1
}
finally {
    Remove-TemporaryBundle
}

