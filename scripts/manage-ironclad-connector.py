#!/usr/bin/env python3
"""Interactive cross-platform installer for the Ironclad CLM custom connector."""

from __future__ import annotations

import argparse
import importlib
import itertools
import json
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Optional


POWER_APPS_URL = "https://api.powerapps.com"
POWER_APPS_API_VERSION = "2016-11-01"
REDIRECT_URL_PREFIX = "https://global.consent.azure-apim.net/redirect/"
MANAGED_VENV_ENVIRONMENT_VARIABLE = "IRONCLADCLM_MANAGED_VENV"


@dataclass(frozen=True)
class EnvironmentRecord:
    display_name: str
    environment_id: str
    environment_type: str
    location: str
    source: str
    raw: Any


@dataclass(frozen=True)
class ConnectorRecord:
    display_name: str
    connector_id: str
    created_by: str
    environment_id: str
    raw: Any


@dataclass(frozen=True)
class BundlePaths:
    working_root: Path
    connector_root: Path
    api_definition: Path
    api_properties: Path
    script: Path
    icon: Path


@dataclass(frozen=True)
class ResolvedConnector:
    connector_id: str
    settings_file: Path
    connector_record: Optional[ConnectorRecord]


@dataclass(frozen=True)
class DeploymentResult:
    environment_name: str
    environment_id: str
    mode: str
    connector_id: Optional[str]
    settings_file: Optional[Path]
    redirect_urls: list[str]
    status: str
    error: Optional[str] = None


_console: Any = None  # rich.console.Console instance, set once Rich is installed.


def init_rich_console() -> None:
    """Initialise the module-level Rich console. Called after Rich is installed."""
    global _console
    try:
        from rich.console import Console  # noqa: PLC0415
        _console = Console(highlight=False)
    except ImportError:
        pass


def write_section(message: str) -> None:
    if _console:
        from rich.rule import Rule  # noqa: PLC0415
        _console.print()
        _console.print(Rule(f"[bold cyan]{message}[/bold cyan]", style="cyan"))
    else:
        print(f"\n== {message} ==")


def write_step(message: str) -> None:
    if _console:
        _console.print(f"\n[bold blue]▶[/bold blue] [bold]{message}[/bold]")
    else:
        print(f"[Step] {message}")


def write_detail(message: str) -> None:
    if _console:
        _console.print(f"  [dim cyan]•[/dim cyan] {message}")
    else:
        print(f"  - {message}")


def write_success(message: str) -> None:
    if _console:
        _console.print(f"  [bold green]✓[/bold green] {message}")
    else:
        print(f"  ✓ {message}")


def write_error(message: str) -> None:
    if _console:
        _console.print(f"  [bold red]✗[/bold red] {message}")
    else:
        print(f"  ✗ {message}")


def _render_progress_bar_plain(label: str, current: int, total: int) -> None:
    bar_width = 28
    if total > 0:
        ratio = min(max(current / total, 0), 1)
        filled = int(bar_width * ratio)
        bar = "#" * filled + "-" * (bar_width - filled)
        percent = int(ratio * 100)
        print(
            f"\r  - {label}: [{bar}] {percent:3d}% ({current / 1024 / 1024:.1f} MB / {total / 1024 / 1024:.1f} MB)",
            end="",
            flush=True,
        )
    else:
        print(
            f"\r  - {label}: downloaded {current / 1024 / 1024:.1f} MB",
            end="",
            flush=True,
        )


def download_with_progress(request: urllib.request.Request, destination: Path, label: str) -> None:
    if _console:
        from rich.progress import (  # noqa: PLC0415
            BarColumn,
            DownloadColumn,
            Progress,
            TextColumn,
            TimeRemainingColumn,
            TransferSpeedColumn,
        )
        with urllib.request.urlopen(request, timeout=60) as response, destination.open("wb") as handle:
            total_raw = response.headers.get("Content-Length", "0")
            total = int(total_raw) if total_raw else 0
            with Progress(
                TextColumn("[bold cyan]{task.description}"),
                BarColumn(bar_width=36, style="cyan", complete_style="bold cyan"),
                "[progress.percentage]{task.percentage:>3.0f}%",
                DownloadColumn(),
                TransferSpeedColumn(),
                TimeRemainingColumn(),
                console=_console,
                transient=False,
            ) as progress:
                task = progress.add_task(label, total=total if total > 0 else None)
                while True:
                    chunk = response.read(1024 * 128)
                    if not chunk:
                        break
                    handle.write(chunk)
                    progress.update(task, advance=len(chunk))
    else:
        with urllib.request.urlopen(request, timeout=60) as response, destination.open("wb") as handle:
            total = int(response.headers.get("Content-Length", "0"))
            downloaded = 0
            _render_progress_bar_plain(label, downloaded, total)
            while True:
                chunk = response.read(1024 * 128)
                if not chunk:
                    break
                handle.write(chunk)
                downloaded += len(chunk)
                _render_progress_bar_plain(label, downloaded, total)
        _render_progress_bar_plain(label, downloaded, total)
        print()


def run_subprocess_with_spinner(command: list[str], status_message: str) -> subprocess.CompletedProcess[str]:
    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    if _console:
        from rich.status import Status  # noqa: PLC0415
        with Status(f"  [bold]{status_message}[/bold]", console=_console, spinner="dots"):
            while process.poll() is None:
                time.sleep(0.1)
    else:
        spinner = itertools.cycle("|/-\\")
        while process.poll() is None:
            print(f"\r  - {status_message} {next(spinner)}", end="", flush=True)
            time.sleep(0.12)

    stdout, stderr = process.communicate()
    if process.returncode == 0:
        if _console:
            _console.print(f"  [bold green]✓[/bold green] {status_message}")
        else:
            print(f"\r  - {status_message} done{' ' * 20}")
    else:
        if _console:
            _console.print(f"  [bold red]✗[/bold red] {status_message}")
        else:
            print(f"\r  - {status_message} failed{' ' * 18}")

    return subprocess.CompletedProcess(command, process.returncode, stdout=stdout, stderr=stderr)


def run_subprocess_with_live_output(
    command: list[str],
    status_message: str,
) -> subprocess.CompletedProcess[str]:
    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
    )
    output_queue: queue.Queue[Optional[str]] = queue.Queue()
    captured_lines: list[str] = []

    def reader() -> None:
        assert process.stdout is not None
        for line in process.stdout:
            output_queue.put(line.rstrip("\n"))
        output_queue.put(None)

    thread = threading.Thread(target=reader, daemon=True)
    thread.start()

    finished_reading = False
    if _console:
        from rich.status import Status  # noqa: PLC0415
        with Status(f"  [bold]{status_message}[/bold]", console=_console, spinner="dots"):
            while not finished_reading:
                try:
                    line = output_queue.get(timeout=0.15)
                    if line is None:
                        finished_reading = True
                        continue
                    captured_lines.append(line)
                    _console.print(f"    [dim]{line}[/dim]")
                except queue.Empty:
                    if process.poll() is not None:
                        finished_reading = True
    else:
        spinner = itertools.cycle("|/-\\")
        while not finished_reading:
            try:
                line = output_queue.get(timeout=0.15)
                if line is None:
                    finished_reading = True
                    continue
                captured_lines.append(line)
                print("\r" + " " * 120, end="\r", flush=True)
                print(f"    {line}")
            except queue.Empty:
                if process.poll() is None:
                    print(f"\r  - {status_message} {next(spinner)}", end="", flush=True)
                else:
                    finished_reading = True

    return_code = process.wait()
    if return_code == 0:
        if _console:
            _console.print(f"  [bold green]✓[/bold green] {status_message}")
        else:
            print("\r" + " " * 120, end="\r", flush=True)
            print(f"  - {status_message} done")
    else:
        if _console:
            _console.print(f"  [bold red]✗[/bold red] {status_message}")
        else:
            print("\r" + " " * 120, end="\r", flush=True)
            print(f"  - {status_message} failed")

    combined_output = "\n".join(captured_lines)
    return subprocess.CompletedProcess(command, return_code, stdout=combined_output, stderr="")


def get_default_state_root() -> Path:
    if sys.platform == "win32":
        app_data = os.environ.get("APPDATA")
        if app_data:
            return Path(app_data) / "IroncladCLM"
        return Path.home() / "AppData" / "Roaming" / "IroncladCLM"

    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support" / "IroncladCLM"

    xdg_state_home = os.environ.get("XDG_STATE_HOME")
    if xdg_state_home:
        return Path(xdg_state_home) / "IroncladCLM"
    return Path.home() / ".local" / "state" / "IroncladCLM"


def get_managed_venv_directory(state_root: Path) -> Path:
    return state_root / "tooling" / "venv"


def get_managed_venv_python(state_root: Path) -> Path:
    venv_directory = get_managed_venv_directory(state_root)
    if sys.platform == "win32":
        return venv_directory / "Scripts" / "python.exe"
    return venv_directory / "bin" / "python"


def build_pip_install_command(package_name: str) -> list[str]:
    return [
        "-m",
        "pip",
        "install",
        "--upgrade",
        "--retries",
        "5",
        "--timeout",
        "60",
        package_name,
    ]


def ensure_managed_runtime(state_root: Path) -> None:
    if os.environ.get(MANAGED_VENV_ENVIRONMENT_VARIABLE) == "1":
        return

    venv_directory = get_managed_venv_directory(state_root)
    venv_python = get_managed_venv_python(state_root)

    write_step("Preparing the private Python environment used for installer dependencies.")
    write_detail(f"Installer packages will be isolated in '{venv_directory}'.")

    if not venv_python.exists():
        venv_directory.parent.mkdir(parents=True, exist_ok=True)
        completed = run_subprocess_with_spinner(
            [sys.executable, "-m", "venv", str(venv_directory)],
            "Creating the private Python environment",
        )
        if completed.returncode != 0:
            for line in completed.stdout.splitlines():
                print(f"    {line}")
            for line in completed.stderr.splitlines():
                print(f"    {line}")
            raise RuntimeError(
                f"Failed to create the private Python environment at '{venv_directory}'."
            )

    environment = os.environ.copy()
    environment[MANAGED_VENV_ENVIRONMENT_VARIABLE] = "1"
    os.execvpe(
        str(venv_python),
        [str(venv_python), str(Path(__file__).resolve()), *sys.argv[1:]],
        environment,
    )


def run_python_module(
    arguments: list[str],
    *,
    interactive: bool = False,
    stream_output: bool = False,
    status_message: Optional[str] = None,
) -> list[str]:
    command = [sys.executable, *arguments]
    if interactive:
        completed = subprocess.run(command, check=False)
        if completed.returncode != 0:
            raise RuntimeError(f"Python command failed with exit code {completed.returncode}.")
        return []

    if stream_output:
        completed = run_subprocess_with_live_output(
            command,
            status_message or "Running Python command",
        )
        if completed.returncode != 0:
            for line in completed.stdout.splitlines():
                print(f"    {line}")
            raise RuntimeError(f"Python command failed with exit code {completed.returncode}.")
        return []

    completed = subprocess.run(
        command,
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode != 0:
        for line in completed.stdout.splitlines():
            print(f"    {line}")
        for line in completed.stderr.splitlines():
            print(f"    {line}")
        raise RuntimeError(f"Python command failed with exit code {completed.returncode}.")

    output = completed.stdout.splitlines()
    output.extend(completed.stderr.splitlines())
    return output


def ensure_pip_available() -> None:
    try:
        run_python_module(["-m", "pip", "--version"])
    except RuntimeError:
        write_detail("pip was not available. Bootstrapping pip with ensurepip.")
        run_python_module(["-m", "ensurepip", "--upgrade"])


def ensure_python_package(module_name: str, package_name: Optional[str] = None) -> Any:
    package_name = package_name or module_name
    try:
        return importlib.import_module(module_name)
    except ImportError:
        write_detail(f"Installing Python package '{package_name}'.")
        ensure_pip_available()
        run_python_module(
            build_pip_install_command(package_name),
            stream_output=True,
            status_message=f"Installing '{package_name}' into the private installer environment.",
        )
        importlib.invalidate_caches()
        return importlib.import_module(module_name)


def ensure_paconn_installed() -> None:
    write_step("Checking for paconn.")
    write_detail(f"Using Python interpreter '{sys.executable}'.")

    try:
        run_python_module(["-m", "paconn", "--version"])
    except RuntimeError:
        write_detail("paconn is not installed. Installing it into the private installer environment.")
        ensure_pip_available()
        run_python_module(
            build_pip_install_command("paconn"),
            stream_output=True,
            status_message="Installing 'paconn' into the private installer environment.",
        )

    version_output = run_python_module(["-m", "paconn", "--version"])
    write_detail(f"paconn is ready. Version: {' '.join(version_output).strip()}")


def invoke_paconn_login() -> None:
    write_step("Running paconn login.")
    write_detail("If you are not already signed in, paconn will show a device-code login prompt.")
    run_python_module(["-m", "paconn", "login"], interactive=True)


def get_paconn_config_directory() -> Path:
    candidates = [
        Path.home() / ".paconn",
        Path.home() / ".powerplatform" / "powershell" / "paconn",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return candidates[0]


def find_access_token(value: Any) -> Optional[str]:
    if isinstance(value, dict):
        access_token = value.get("access_token")
        if isinstance(access_token, str) and access_token.strip():
            return access_token
        for nested_value in value.values():
            token = find_access_token(nested_value)
            if token:
                return token
    elif isinstance(value, list):
        for nested_value in value:
            token = find_access_token(nested_value)
            if token:
                return token
    return None


def get_paconn_access_token() -> dict[str, str]:
    config_dir = get_paconn_config_directory()
    token_file = config_dir / "accessTokens.json"
    if not token_file.exists():
        raise RuntimeError(f"The paconn access token file was not found at '{token_file}'.")

    token_data = json.loads(token_file.read_text(encoding="utf-8"))
    access_token = find_access_token(token_data)
    if not access_token:
        raise RuntimeError(f"The paconn token file at '{token_file}' does not contain an access token.")

    token_type = "Bearer"
    if isinstance(token_data, dict):
        token_type = token_data.get("token_type", token_type)

    return {
        "token_type": token_type,
        "access_token": access_token,
    }


def invoke_power_apps_request(uri: str, token: dict[str, str]) -> Any:
    request = urllib.request.Request(
        uri,
        headers={
            "Authorization": f"{token['token_type']} {token['access_token']}",
            "x-ms-origin": "paconn-cli",
            "Accept": "application/json",
        },
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        return json.loads(response.read().decode("utf-8"))


def get_object_property_value(obj: Any, name: str) -> Any:
    if obj is None:
        return None
    if isinstance(obj, dict):
        return obj.get(name)
    return getattr(obj, name, None)


def convert_to_environment_record(environment: Any, source: str) -> Optional[EnvironmentRecord]:
    properties = get_object_property_value(environment, "properties") or {}
    environment_id = get_object_property_value(environment, "name")
    if not environment_id:
        environment_id = get_object_property_value(properties, "environmentId")
    if not environment_id:
        environment_id = get_object_property_value(environment, "id")
    if not environment_id:
        return None

    display_name = get_object_property_value(properties, "displayName")
    if not display_name:
        display_name = get_object_property_value(environment, "displayName")
    if not display_name:
        display_name = environment_id

    environment_type = get_object_property_value(properties, "environmentType")
    if not environment_type:
        environment_type = get_object_property_value(properties, "environmentSku")
    if not environment_type:
        environment_type = get_object_property_value(environment, "type") or ""

    location = get_object_property_value(properties, "azureRegionHint")
    if not location:
        location = get_object_property_value(properties, "location")
    if not location:
        location = get_object_property_value(environment, "location") or ""

    return EnvironmentRecord(
        display_name=str(display_name),
        environment_id=str(environment_id),
        environment_type=str(environment_type),
        location=str(location),
        source=source,
        raw=environment,
    )


def get_accessible_environments(token: dict[str, str]) -> list[EnvironmentRecord]:
    write_step("Retrieving the environments you can deploy to.")

    request_targets = [
        {
            "name": "Power Apps RP",
            "uri": f"{POWER_APPS_URL}/providers/Microsoft.PowerApps/environments?api-version={POWER_APPS_API_VERSION}",
        },
        {
            "name": "Business App Platform user scope",
            "uri": "https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/user/environments?api-version=2020-10-01",
        },
    ]

    last_error: Optional[Exception] = None
    for target in request_targets:
        try:
            write_detail(f"Trying environment discovery via {target['name']}.")
            response = invoke_power_apps_request(target["uri"], token)
            items = get_object_property_value(response, "value") or []
            if not items and isinstance(response, list):
                items = response

            environments = [
                record
                for item in items
                if (record := convert_to_environment_record(item, target["name"])) is not None
            ]
            deduplicated = {
                (environment.display_name, environment.environment_id): environment
                for environment in sorted(environments, key=lambda item: (item.display_name.lower(), item.environment_id))
            }

            if deduplicated:
                result = list(deduplicated.values())
                write_detail(f"Found {len(result)} environments.")
                return result
        except Exception as error:  # noqa: BLE001
            last_error = error
            write_detail(f"Environment discovery via {target['name']} failed: {error}")

    if last_error is None:
        raise RuntimeError("Unable to retrieve Power Platform environments after paconn login.")
    raise RuntimeError(f"Unable to retrieve Power Platform environments after paconn login. {last_error}")


def ensure_rich() -> None:
    write_step("Checking the Rich terminal library used for coloured output.")
    ensure_python_package("rich")
    init_rich_console()


def prompt_text(prompt_message: str, default: Optional[str] = None) -> str:
    if _console:
        from rich.prompt import Prompt  # noqa: PLC0415

        return str(Prompt.ask(prompt_message, default=default))

    response = input(f"{prompt_message}: ").strip()
    if response:
        return response
    if default is not None:
        return default
    raise RuntimeError(f"No value was provided for '{prompt_message}'.")


def prompt_confirm(prompt_message: str, default: bool = False) -> bool:
    if _console:
        from rich.prompt import Confirm  # noqa: PLC0415

        return bool(Confirm.ask(prompt_message, default=default))

    suffix = " [Y/n]" if default else " [y/N]"
    response = input(f"{prompt_message}{suffix}: ").strip().lower()
    if not response:
        return default
    return response in {"y", "yes"}


def parse_selection_input(raw_value: str, maximum: int) -> list[int]:
    selected: set[int] = set()
    for part in [segment.strip() for segment in raw_value.split(",") if segment.strip()]:
        if "-" in part:
            start_text, end_text = [segment.strip() for segment in part.split("-", 1)]
            start = int(start_text)
            end = int(end_text)
            if start > end:
                raise ValueError(f"Invalid range '{part}'.")
            for index in range(start, end + 1):
                if index < 1 or index > maximum:
                    raise ValueError(f"Selection '{index}' is out of range.")
                selected.add(index)
        else:
            index = int(part)
            if index < 1 or index > maximum:
                raise ValueError(f"Selection '{index}' is out of range.")
            selected.add(index)

    if not selected:
        raise ValueError("At least one selection is required.")
    return sorted(selected)


def render_selection_table(title: str, columns: list[str], rows: list[list[str]]) -> None:
    if _console:
        from rich.table import Table  # noqa: PLC0415

        table = Table(title=title, show_header=True, header_style="bold cyan", border_style="bright_blue")
        for column in columns:
            table.add_column(column, overflow="fold")
        for row in rows:
            table.add_row(*row)
        _console.print(table)
        return

    print(title)
    print(" | ".join(columns))
    for row in rows:
        print(" | ".join(row))


def select_action() -> str:
    write_step("Asking whether you want to install a new connector or update an existing one.")
    render_selection_table(
        "Connector action",
        ["#", "Action", "Description"],
        [
            ["1", "Install", "Create a new Ironclad CLM custom connector in one or more environments."],
            ["2", "Update", "Update an existing Ironclad CLM custom connector with the latest GitHub payload."],
        ],
    )
    selection = prompt_text("Enter the action number", default="1").strip()
    if selection == "1":
        return "Install"
    if selection == "2":
        return "Update"
    raise RuntimeError("No valid action was selected. The installer cannot continue.")


def select_environments(environments: list[EnvironmentRecord]) -> list[EnvironmentRecord]:
    write_step("Presenting the environment list. Choose one or more environment numbers separated by commas.")
    render_selection_table(
        "Available Power Platform environments",
        ["#", "Name", "Environment ID", "Type", "Location", "Source"],
        [
            [
                str(index),
                environment.display_name,
                environment.environment_id,
                environment.environment_type or "—",
                environment.location or "—",
                environment.source,
            ]
            for index, environment in enumerate(environments, start=1)
        ],
    )
    write_detail("Example selections: 1 or 1,3,5 or 2-4")
    raw_selection = prompt_text("Enter the environment numbers").strip()
    try:
        indices = parse_selection_input(raw_selection, len(environments))
    except ValueError as error:
        raise RuntimeError(f"No valid environments were selected. {error}") from error
    return [environments[index - 1] for index in indices]


def select_connector_registration(
    environment_id: str,
    connectors: list[ConnectorRecord],
) -> ConnectorRecord:
    write_step(
        f"More than one matching connector was found in environment '{environment_id}'. Asking you to pick the one to update."
    )
    render_selection_table(
        f"Connectors in {environment_id}",
        ["#", "Name", "Connector ID", "Created By"],
        [
            [
                str(index),
                connector.display_name or "—",
                connector.connector_id,
                connector.created_by or "—",
            ]
            for index, connector in enumerate(connectors, start=1)
        ],
    )
    selection = prompt_text("Enter the connector number").strip()
    try:
        index = int(selection)
    except ValueError as error:
        raise RuntimeError(f"No connector was selected for environment '{environment_id}'.") from error
    if index < 1 or index > len(connectors):
        raise RuntimeError(f"No connector was selected for environment '{environment_id}'.")
    return connectors[index - 1]


def select_existing_settings_file() -> Optional[Path]:
    use_existing_settings = prompt_confirm(
        "Do you want to import an existing settings.json file for this deployment?",
        default=False,
    )
    if not use_existing_settings:
        return None

    selected_path = prompt_text("Enter the full path to the existing settings.json file").strip()
    if not selected_path:
        raise RuntimeError("A settings.json path was requested but no path was provided.")
    return Path(str(selected_path)).expanduser()


def new_temporary_working_directory() -> Path:
    return Path(tempfile.mkdtemp(prefix="ironcladclm-"))


def download_connector_bundle(repo_owner: str, repo_name: str, ref: str) -> BundlePaths:
    write_step("Downloading the latest connector payload from GitHub.")

    working_root = new_temporary_working_directory()
    archive_path = working_root / "connector.zip"
    extract_path = working_root / "bundle"
    zip_url = f"https://api.github.com/repos/{repo_owner}/{repo_name}/zipball/{ref}"

    request = urllib.request.Request(
        zip_url,
        headers={
            "User-Agent": "IroncladCLM-Installer",
            "Accept": "application/vnd.github+json",
        },
    )
    download_with_progress(request, archive_path, "Downloading connector payload")

    write_detail("Extracting the downloaded connector bundle.")
    with zipfile.ZipFile(archive_path) as archive:
        archive.extractall(extract_path)

    bundle_roots = [path for path in extract_path.iterdir() if path.is_dir()]
    if not bundle_roots:
        raise RuntimeError("The downloaded GitHub archive did not contain a top-level folder.")

    connector_root = bundle_roots[0] / "connector"
    bundle = BundlePaths(
        working_root=working_root,
        connector_root=connector_root,
        api_definition=connector_root / "apiDefinition.swagger.json",
        api_properties=connector_root / "apiProperties.json",
        script=connector_root / "script.csx",
        icon=connector_root / "icon.png",
    )
    for required_path in (bundle.api_definition, bundle.api_properties, bundle.script, bundle.icon):
        if not required_path.exists():
            raise RuntimeError(f"The downloaded GitHub payload is missing '{required_path}'.")

    write_detail(f"Using connector assets from {connector_root}")
    return bundle


def get_deployment_directory(state_root: Path) -> Path:
    deployment_directory = state_root / "deployments"
    deployment_directory.mkdir(parents=True, exist_ok=True)
    return deployment_directory


def announce_deployment_directory(state_root: Path) -> Path:
    deployment_directory = get_deployment_directory(state_root)
    write_step("Preparing the local deployment settings folder used for later updates.")
    write_detail(f"Settings files will be stored in '{deployment_directory}'.")
    return deployment_directory


def get_settings_file_path(state_root: Path, environment_id: str) -> Path:
    return get_deployment_directory(state_root) / f"{environment_id}_settings.json"


def write_settings_file(
    settings_file_path: Path,
    environment_id: str,
    bundle: BundlePaths,
    connector_id: Optional[str] = None,
) -> None:
    settings: dict[str, str] = {
        "environment": environment_id,
        "apiProperties": str(bundle.api_properties),
        "apiDefinition": str(bundle.api_definition),
        "icon": str(bundle.icon),
        "powerAppsUrl": POWER_APPS_URL,
        "powerAppsApiVersion": POWER_APPS_API_VERSION,
        "script": str(bundle.script),
    }
    if connector_id:
        settings["connectorId"] = connector_id

    settings_file_path.write_text(json.dumps(settings, indent=2), encoding="utf-8")


def load_existing_settings_file(settings_file_path: Path) -> dict[str, Any]:
    if not settings_file_path.exists():
        raise RuntimeError(f"The supplied settings file '{settings_file_path}' does not exist.")
    if not settings_file_path.is_file():
        raise RuntimeError(f"The supplied settings path '{settings_file_path}' is not a file.")

    try:
        settings = json.loads(settings_file_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        raise RuntimeError(f"The supplied settings file '{settings_file_path}' is not valid JSON: {error}") from error

    if not isinstance(settings, dict):
        raise RuntimeError(f"The supplied settings file '{settings_file_path}' does not contain a JSON object.")
    return settings


def import_existing_settings_file(
    source_settings_file: Path,
    target_settings_file: Path,
    environment_id: str,
    bundle: BundlePaths,
) -> Optional[str]:
    settings = load_existing_settings_file(source_settings_file)
    source_environment = settings.get("environment")
    if isinstance(source_environment, str) and source_environment.strip() and source_environment != environment_id:
        raise RuntimeError(
            f"The supplied settings file targets environment '{source_environment}', not '{environment_id}'."
        )

    connector_id = settings.get("connectorId")
    normalized_connector_id = connector_id if isinstance(connector_id, str) and connector_id.strip() else None
    write_detail(f"Importing settings from '{source_settings_file}'.")
    write_settings_file(target_settings_file, environment_id, bundle, normalized_connector_id)
    write_detail(f"Imported settings were copied to '{target_settings_file}'.")
    return normalized_connector_id


def get_connector_records_from_response(response: Any) -> list[Any]:
    items = get_object_property_value(response, "value") or []
    if not items:
        items = get_object_property_value(response, "apis") or []
    return list(items)


def get_connector_registrations(token: dict[str, str], environment_id: str) -> list[ConnectorRecord]:
    filter_value = urllib.parse.quote(f"environment eq '{environment_id}'", safe="")
    uri = f"{POWER_APPS_URL}/providers/Microsoft.PowerApps/apis?api-version={POWER_APPS_API_VERSION}&$filter={filter_value}"
    response = invoke_power_apps_request(uri, token)

    connectors: list[ConnectorRecord] = []
    for item in get_connector_records_from_response(response):
        properties = get_object_property_value(item, "properties") or {}
        created_by = get_object_property_value(get_object_property_value(properties, "createdBy") or {}, "displayName")
        connectors.append(
            ConnectorRecord(
                display_name=str(get_object_property_value(properties, "displayName") or ""),
                connector_id=str(get_object_property_value(item, "name") or ""),
                created_by=str(created_by or ""),
                environment_id=environment_id,
                raw=item,
            )
        )
    return connectors


def resolve_connector_id_for_update(
    token: dict[str, str],
    environment_id: str,
    bundle: BundlePaths,
    connector_name: str,
    state_root: Path,
    existing_settings_file: Optional[Path],
) -> ResolvedConnector:
    settings_file_path = get_settings_file_path(state_root, environment_id)
    if existing_settings_file is not None:
        imported_connector_id = import_existing_settings_file(
            existing_settings_file,
            settings_file_path,
            environment_id,
            bundle,
        )
        if imported_connector_id:
            return ResolvedConnector(imported_connector_id, settings_file_path, None)
        write_detail(
            "The imported settings file did not contain a connectorId, so the installer will look up the connector in Power Platform."
        )

    if settings_file_path.exists():
        saved_settings = json.loads(settings_file_path.read_text(encoding="utf-8"))
        saved_connector_id = saved_settings.get("connectorId")
        if isinstance(saved_connector_id, str) and saved_connector_id.strip():
            write_detail(f"Using saved settings file '{settings_file_path}'.")
            write_settings_file(settings_file_path, environment_id, bundle, saved_connector_id)
            return ResolvedConnector(saved_connector_id, settings_file_path, None)

    write_detail(f"No saved settings were found for environment '{environment_id}'. Looking for existing connectors in that environment.")
    connectors = get_connector_registrations(token, environment_id)
    matches = [
        connector
        for connector in connectors
        if connector.display_name == connector_name or connector.connector_id.startswith("shared_ironclad")
    ]
    if not matches:
        raise RuntimeError(
            f"No existing '{connector_name}' connector was found in environment '{environment_id}', and no saved settings file exists."
        )

    selected_connector = matches[0] if len(matches) == 1 else select_connector_registration(environment_id, matches)
    write_settings_file(settings_file_path, environment_id, bundle, selected_connector.connector_id)
    return ResolvedConnector(selected_connector.connector_id, settings_file_path, selected_connector)


def invoke_paconn_deployment(mode: str, settings_file_path: Path, connector_secret: str) -> None:
    verb = "create" if mode == "Install" else "update"
    write_step(f"Running paconn {verb}.")

    arguments = ["-m", "paconn", verb, "-s", str(settings_file_path)]
    if mode == "Install":
        arguments.extend(["--secret", connector_secret, "--overwrite-settings"])

    output = run_python_module(arguments)
    for line in output:
        print(f"    {line}")


def get_connector_redirect_urls(connector_id: str) -> list[str]:
    write_step("Deriving the generated redirect URL from the deployed connector ID.")
    redirect_id = connector_id[7:] if connector_id.lower().startswith("shared_") else connector_id
    return [f"{REDIRECT_URL_PREFIX}{redirect_id}"]


def show_intro() -> None:
    if _console:
        from rich.align import Align  # noqa: PLC0415
        from rich.panel import Panel  # noqa: PLC0415
        from rich.text import Text  # noqa: PLC0415
        body = Text(justify="center")
        body.append("Downloads the latest connector payload from GitHub,\n", style="white")
        body.append("verifies ", style="dim white")
        body.append("paconn", style="bold cyan")
        body.append(", authenticates, and lets you pick\nenvironments to ", style="dim white")
        body.append("install", style="bold green")
        body.append(" or ", style="dim white")
        body.append("update", style="bold yellow")
        body.append(" the Ironclad CLM connector.", style="dim white")
        _console.print()
        _console.print(
            Panel(
                Align.center(body),
                title="[bold cyan]🔗  Ironclad CLM[/bold cyan]  [dim white]•  Custom Connector Manager[/dim white]",
                border_style="bright_blue",
                padding=(1, 4),
            )
        )
        _console.print()
    else:
        write_section("Ironclad CLM custom connector installer")
        print(
            "This script downloads the latest connector payload from GitHub, checks paconn, signs you in, "
            "lets you pick environments, and then installs or updates the Ironclad CLM connector."
        )
        print(
            "For each completed deployment it saves an environment-specific settings file and then derives "
            "the generated redirect URL from the deployed connector ID so it can print it immediately."
        )


def remove_temporary_bundle(bundle: BundlePaths | None) -> None:
    if bundle and bundle.working_root.exists():
        shutil.rmtree(bundle.working_root, ignore_errors=True)


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Install or update the Ironclad CLM custom connector.")
    parser.add_argument("--repo-owner", default="maxhenkentech", help="GitHub repository owner.")
    parser.add_argument("--repo-name", default="MSPP-IroncladCLM", help="GitHub repository name.")
    parser.add_argument("--ref", default="main", help="Git reference to download.")
    parser.add_argument("--connector-name", default="Ironclad CLM", help="Connector display name.")
    parser.add_argument(
        "--state-root",
        default=str(get_default_state_root()),
        help="Directory used to store environment-specific deployment settings.",
    )
    parser.add_argument(
        "--connector-secret",
        default="dummy",
        help="OAuth client secret placeholder passed to paconn create.",
    )
    parser.add_argument(
        "--settings-file",
        help="Path to an existing settings.json file to import for a single-environment deployment.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_arguments()
    state_root = Path(args.state_root).expanduser()
    bundle: Optional[BundlePaths] = None

    try:
        ensure_managed_runtime(state_root)
        ensure_rich()
        show_intro()
        ensure_paconn_installed()
        invoke_paconn_login()
        deployment_directory = announce_deployment_directory(state_root)

        token = get_paconn_access_token()
        mode = select_action()
        environments = get_accessible_environments(token)
        selected_environments = select_environments(environments)
        existing_settings_file: Optional[Path] = None
        if mode == "Update":
            existing_settings_file = Path(args.settings_file).expanduser() if args.settings_file else None
            if existing_settings_file is None:
                existing_settings_file = select_existing_settings_file()
            if existing_settings_file is not None and len(selected_environments) != 1:
                raise RuntimeError(
                    "An existing settings.json file can only be imported when exactly one environment is selected."
                )
        elif args.settings_file:
            raise RuntimeError("The --settings-file option is only supported when running an update.")
        bundle = download_connector_bundle(args.repo_owner, args.repo_name, args.ref)

        results: list[DeploymentResult] = []
        for environment in selected_environments:
            environment_id = environment.environment_id
            write_section(f"Processing environment {environment_id}")
            write_detail(f"Environment name: {environment.display_name}")

            settings_file_path = get_settings_file_path(state_root, environment_id)
            connector_id: Optional[str] = None
            try:
                if mode == "Install":
                    write_settings_file(settings_file_path, environment_id, bundle)
                else:
                    resolved_connector = resolve_connector_id_for_update(
                        token,
                        environment_id,
                        bundle,
                        args.connector_name,
                        state_root,
                        existing_settings_file,
                    )
                    settings_file_path = resolved_connector.settings_file
                    connector_id = resolved_connector.connector_id

                invoke_paconn_deployment(mode, settings_file_path, args.connector_secret)

                saved_settings = json.loads(settings_file_path.read_text(encoding="utf-8"))
                saved_connector_id = saved_settings.get("connectorId")
                if not isinstance(saved_connector_id, str) or not saved_connector_id.strip():
                    raise RuntimeError(f"paconn completed but the connector ID was not written to '{settings_file_path}'.")
                connector_id = saved_connector_id

                redirect_urls: list[str] = []
                try:
                    redirect_urls = get_connector_redirect_urls(connector_id)
                except Exception as error:  # noqa: BLE001
                    write_detail(f"The connector was deployed but the redirect URL could not be derived: {error}")

                results.append(
                    DeploymentResult(
                        environment_name=environment.display_name,
                        environment_id=environment_id,
                        mode=mode,
                        connector_id=connector_id,
                        settings_file=settings_file_path,
                        redirect_urls=redirect_urls,
                        status="Succeeded",
                    )
                )

                write_detail(f"Deployment finished. Settings file: {settings_file_path}")
                write_detail(f"Saved deployment settings under '{deployment_directory}'.")
                if redirect_urls:
                    for redirect_url in redirect_urls:
                        write_success(f"Redirect URL: {redirect_url}")
                else:
                    write_detail("Redirect URL: No redirect URL could be derived automatically.")
            except Exception as error:  # noqa: BLE001
                results.append(
                    DeploymentResult(
                        environment_name=environment.display_name,
                        environment_id=environment_id,
                        mode=mode,
                        connector_id=connector_id,
                        settings_file=settings_file_path if settings_file_path.exists() else None,
                        redirect_urls=[],
                        status="Failed",
                        error=str(error),
                    )
                )
                write_error(f"Deployment failed for environment {environment_id}: {error}")

        write_section("Deployment Summary")
        if _console:
            import rich.box  # noqa: PLC0415
            from rich.table import Table  # noqa: PLC0415
            table = Table(
                show_header=True,
                header_style="bold cyan",
                box=rich.box.ROUNDED,
                border_style="cyan",
                show_lines=True,
            )
            table.add_column("Environment", style="bold white", no_wrap=False)
            table.add_column("Status", no_wrap=True)
            table.add_column("Mode", style="dim white", no_wrap=True)
            table.add_column("Connector ID", style="dim", no_wrap=False)
            table.add_column("Redirect URL", style="cyan", no_wrap=False)
            for result in results:
                status_cell = (
                    "[bold green]✓  Succeeded[/bold green]"
                    if result.status == "Succeeded"
                    else "[bold red]✗  Failed[/bold red]"
                )
                redirect_cell = (
                    result.redirect_urls[0]
                    if result.redirect_urls
                    else ("—" if result.status == "Succeeded" else "")
                )
                env_cell = f"{result.environment_name}\n[dim]{result.environment_id}[/dim]"
                table.add_row(env_cell, status_cell, result.mode, result.connector_id or "—", redirect_cell)
            _console.print(table)
            for result in results:
                if result.settings_file:
                    write_detail(f"Settings file ({result.environment_name}): {result.settings_file}")
                if result.error:
                    write_error(f"{result.environment_name}: {result.error}")
        else:
            for result in results:
                print(f"[{result.status}] {result.environment_name} ({result.environment_id})")
                if result.connector_id:
                    write_detail(f"Connector ID: {result.connector_id}")
                if result.settings_file:
                    write_detail(f"Settings file: {result.settings_file}")
                if result.redirect_urls:
                    for redirect_url in result.redirect_urls:
                        write_detail(f"Redirect URL: {redirect_url}")
                elif result.status == "Succeeded":
                    write_detail("Redirect URL: could not be derived automatically.")
                if result.error:
                    write_detail(f"Error: {result.error}")

        if any(result.status == "Failed" for result in results):
            raise RuntimeError("One or more environments failed. Review the summary above.")
        return 0
    except KeyboardInterrupt:
        if _console:
            _console.print("\n[bold yellow]⚠  Installer cancelled by user.[/bold yellow]")
        else:
            print("\nInstaller cancelled by user.")
        return 1
    except Exception as error:  # noqa: BLE001
        if _console:
            _console.print(f"\n[bold red]✗  Installer failed:[/bold red] {error}")
        else:
            print(f"\nInstaller failed: {error}")
        return 1
    finally:
        remove_temporary_bundle(bundle)


if __name__ == "__main__":
    raise SystemExit(main())
