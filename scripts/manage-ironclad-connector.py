#!/usr/bin/env python3
"""Interactive cross-platform installer for the Ironclad CLM custom connector."""

from __future__ import annotations

import argparse
import importlib
import itertools
import json
import os
import queue
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import urllib.request
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Optional


REDIRECT_URL_PREFIX = "https://global.consent.azure-apim.net/redirect/"
MANAGED_VENV_ENVIRONMENT_VARIABLE = "IRONCLADCLM_MANAGED_VENV"


@dataclass(frozen=True)
class BundlePaths:
    working_root: Path
    connector_root: Path
    api_definition: Path
    api_properties: Path
    script: Path
    icon: Path


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


def write_warning(message: str) -> None:
    if _console:
        _console.print(f"  [bold yellow]⚠[/bold yellow] {message}")
    else:
        print(f"  ⚠  {message}")


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


def build_macos_ca_bundle(state_root: Path) -> Optional[Path]:
    """Export macOS system keychains to a PEM bundle for corporate proxy SSL trust."""
    if sys.platform != "darwin":
        return None
    if not shutil.which("security"):
        return None

    ca_bundle_path = state_root / "tooling" / "ca-bundle.pem"
    ca_bundle_path.parent.mkdir(parents=True, exist_ok=True)

    keychains = [
        "/Library/Keychains/System.keychain",
        "/System/Library/Keychains/SystemRootCertificates.keychain",
    ]
    pem_parts: list[str] = []
    for keychain in keychains:
        result = subprocess.run(
            ["security", "export", "-t", "certs", "-f", "pemseq", "-k", keychain],
            capture_output=True,
            text=True,
            check=False,
        )
        if result.returncode == 0 and result.stdout:
            pem_parts.append(result.stdout)

    if not pem_parts:
        return None

    ca_bundle_path.write_text("\n".join(pem_parts))
    return ca_bundle_path


def build_windows_ca_bundle(state_root: Path) -> Optional[Path]:
    """Export Windows certificate stores to a PEM bundle for corporate proxy SSL trust."""
    if sys.platform != "win32":
        return None
    try:
        import base64  # noqa: PLC0415
        import ssl     # noqa: PLC0415

        ca_bundle_path = state_root / "tooling" / "ca-bundle.pem"
        ca_bundle_path.parent.mkdir(parents=True, exist_ok=True)

        pem_parts: list[str] = []
        for store_name in ("ROOT", "CA", "AuthRoot"):
            try:
                for cert_der, encoding, _trust in ssl.enum_certificates(store_name):  # type: ignore[attr-defined]
                    if encoding == "x509_asn":
                        b64 = base64.encodebytes(cert_der).decode("ascii")
                        pem_parts.append(f"-----BEGIN CERTIFICATE-----\n{b64}-----END CERTIFICATE-----\n")
            except Exception:  # noqa: BLE001
                pass

        if not pem_parts:
            return None

        ca_bundle_path.write_text("".join(pem_parts), encoding="utf-8")
        return ca_bundle_path
    except Exception:  # noqa: BLE001
        return None


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

    if not environment.get("REQUESTS_CA_BUNDLE"):
        ca_bundle = build_macos_ca_bundle(state_root) or build_windows_ca_bundle(state_root)
        if ca_bundle:
            environment["REQUESTS_CA_BUNDLE"] = str(ca_bundle)

    if sys.platform == "win32":
        # os.execvpe on Windows exits the parent process, causing CMD to recapture
        # stdin and treat the user's first keystroke as a shell command. Use
        # subprocess.run instead so the parent stays alive and holds stdin.
        result = subprocess.run(
            [str(venv_python), str(Path(__file__).resolve()), *sys.argv[1:]],
            env=environment,
        )
        sys.exit(result.returncode)

    os.execvpe(
        str(venv_python),
        [str(venv_python), str(Path(__file__).resolve()), *sys.argv[1:]],
        environment,
    )


def _paconn_exit_hint(returncode: int, output: str = "") -> str:
    if returncode == 2:
        return "paconn rejected a command-line argument. This may indicate a version mismatch — try upgrading paconn."
    if "unassigned function app" in output.lower():
        return (
            "'Unable to find an unassigned function app in region' is a known, intermittently occurring "
            "Microsoft Power Platform infrastructure issue. This error is often transient — wait a few minutes "
            "and run the installer again. If the error persists after 24 hours, raise a Microsoft support ticket "
            "and ask them to provision a function app slot in your region."
        )
    return "If this looks like a transient API error, wait a moment and run the script again. If authentication has expired, the next run will prompt you to log in again."


_PACONN_SELECTION_RE = re.compile(r"selected\s*:", re.IGNORECASE)
_PACONN_ENV_SELECTED_RE = re.compile(r"Environment selected\s*:", re.IGNORECASE)
_PACONN_CONNECTOR_SELECTED_RE = re.compile(r"Connector selected\s*:", re.IGNORECASE)
_SPINNER_FRAMES = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"]
_SPINNER_MSG = "Pushing connector — this can take some time..."


def run_paconn_interactive(command: list[str], spinner_trigger: Optional[re.Pattern[str]] = None) -> tuple[int, str]:
    """Run paconn with stdin/stdout connected to the terminal.

    Returns (returncode, accumulated_output).  On Unix/macOS, a pty is used so
    that after the user confirms their selection an animated spinner is shown
    while the API call runs.  On Windows, or when stdin is not a tty, execution
    falls back to a plain subprocess and accumulated_output will be empty.
    """
    if sys.platform == "win32" or not sys.stdin.isatty():
        return subprocess.run(command, check=False).returncode, ""

    try:
        import pty    # noqa: PLC0415
        import select  # noqa: PLC0415
        import termios  # noqa: PLC0415
        import tty      # noqa: PLC0415
    except ImportError:
        return subprocess.run(command, check=False).returncode, ""

    master_fd, slave_fd = pty.openpty()
    proc = subprocess.Popen(command, stdin=slave_fd, stdout=slave_fd, stderr=slave_fd, close_fds=True)
    os.close(slave_fd)

    old_settings = termios.tcgetattr(sys.stdin)
    tty.setraw(sys.stdin)

    spinner_active = False
    spinner_idx = 0
    spinner_last = 0.0
    accumulated = ""
    pending: list[bytes] = []
    pty_eof = False

    def _tick_spinner() -> None:
        nonlocal spinner_idx, spinner_last
        now = time.monotonic()
        if now - spinner_last >= 0.1:
            frame = _SPINNER_FRAMES[spinner_idx % len(_SPINNER_FRAMES)]
            sys.stdout.write(f"\r  {frame}  {_SPINNER_MSG}")
            sys.stdout.flush()
            spinner_idx += 1
            spinner_last = now

    try:
        while True:
            timeout = 0.1 if spinner_active else 1.0
            try:
                rlist, _, _ = select.select([master_fd, sys.stdin], [], [], timeout)
            except (ValueError, OSError):
                break

            for fd in rlist:
                if fd == master_fd:
                    try:
                        data = os.read(master_fd, 4096)
                    except OSError:
                        # pty slave closed — process has exited; stop reading
                        pty_eof = True
                        break
                    if data:
                        text = data.decode("utf-8", errors="replace")
                        accumulated += text
                        trigger = spinner_trigger or _PACONN_SELECTION_RE
                        if not spinner_active and trigger.search(accumulated):
                            sys.stdout.buffer.write(b"\r\n")
                            sys.stdout.buffer.flush()
                            spinner_active = True
                        if spinner_active:
                            pending.append(data)
                        else:
                            sys.stdout.buffer.write(data)
                            sys.stdout.buffer.flush()
                elif fd == sys.stdin:
                    try:
                        data = os.read(sys.stdin.fileno(), 1024)
                    except OSError:
                        data = b""
                    if data:
                        os.write(master_fd, data)

            if spinner_active:
                _tick_spinner()

            if pty_eof or proc.poll() is not None:
                if not pty_eof:
                    # Drain any remaining buffered output from the pty.
                    try:
                        while True:
                            r2, _, _ = select.select([master_fd], [], [], 0.05)
                            if not r2:
                                break
                            data = os.read(master_fd, 4096)
                            if not data:
                                break
                            accumulated += data.decode("utf-8", errors="replace")
                            if spinner_active:
                                pending.append(data)
                            else:
                                sys.stdout.buffer.write(data)
                                sys.stdout.buffer.flush()
                    except OSError:
                        pass
                break
    finally:
        try:
            termios.tcsetattr(sys.stdin, termios.TCSADRAIN, old_settings)
        except termios.error:
            pass
        try:
            os.close(master_fd)
        except OSError:
            pass

    if spinner_active:
        sys.stdout.write("\r" + " " * (len(_SPINNER_MSG) + 6) + "\r\n")
        sys.stdout.flush()
        for chunk in pending:
            sys.stdout.buffer.write(chunk)
        sys.stdout.buffer.flush()

    return (proc.returncode if proc.returncode is not None else proc.wait()), accumulated


def run_python_module(
    arguments: list[str],
    *,
    interactive: bool = False,
    stream_output: bool = False,
    status_message: Optional[str] = None,
) -> list[str]:
    command = [sys.executable, *arguments]
    is_paconn = len(arguments) >= 2 and arguments[1] == "paconn"
    if interactive:
        if is_paconn:
            verb = arguments[2] if len(arguments) > 2 else ""
            trigger = _PACONN_CONNECTOR_SELECTED_RE if verb == "update" else _PACONN_ENV_SELECTED_RE
            returncode, _ = run_paconn_interactive(command, spinner_trigger=trigger)
        else:
            returncode = subprocess.run(command, check=False).returncode
        if returncode != 0:
            hint = _paconn_exit_hint(returncode) if is_paconn else ""
            msg = f"paconn exited with code {returncode}." if is_paconn else f"Python command failed with exit code {returncode}."
            raise RuntimeError(f"{msg}{(' ' + hint) if hint else ''}")
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


_PACONN_CREATED_RE = re.compile(r"(shared_\S+)\s+created successfully", re.IGNORECASE)


def invoke_paconn_deployment(mode: str, bundle: BundlePaths, connector_secret: str) -> tuple[Optional[str], Optional[str]]:
    verb = "create" if mode == "Install" else "update"
    write_step(f"Running paconn {verb}.")
    write_detail(
        "paconn will ask you to select an environment"
        + (" and an existing connector to update" if mode == "Update" else "")
        + "."
    )

    file_flags = [
        "--api-prop", str(bundle.api_properties),
        "--api-def", str(bundle.api_definition),
        "--script", str(bundle.script),
        "--icon", str(bundle.icon),
        "--secret", connector_secret if mode == "Install" else "placeholder",
    ]

    trigger = _PACONN_CONNECTOR_SELECTED_RE if mode == "Update" else _PACONN_ENV_SELECTED_RE
    returncode, accumulated = run_paconn_interactive(
        [sys.executable, "-m", "paconn", verb, *file_flags],
        spinner_trigger=trigger,
    )
    if returncode != 0:
        hint = _paconn_exit_hint(returncode, accumulated)
        raise RuntimeError(f"paconn exited with code {returncode}. {hint}")

    if mode == "Update":
        return None, None

    # Parse the connector ID directly from paconn's "shared_xxx created successfully." output.
    match = _PACONN_CREATED_RE.search(accumulated)
    if match:
        return match.group(1).strip(), None

    # Fallback for Windows / non-tty: paconn output wasn't captured, so ask the user.
    # The connector ID was printed by paconn above (e.g. "shared_ironclad-... created successfully.").
    write_warning("The connector ID could not be captured automatically (no pty on this platform).")
    try:
        connector_id = prompt_text("  Paste the connector ID printed by paconn above (or press Enter to skip)")
    except (KeyboardInterrupt, EOFError):
        connector_id = ""
    return (connector_id.strip() or None), None


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
    parser.add_argument(
        "--state-root",
        default=str(get_default_state_root()),
        help="Directory used to store the managed Python environment for installer tooling.",
    )
    parser.add_argument(
        "--connector-secret",
        default="dummy",
        help="OAuth client secret placeholder passed to paconn create.",
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

        mode = select_action()
        bundle = download_connector_bundle(args.repo_owner, args.repo_name, args.ref)

        connector_id, environment_id = invoke_paconn_deployment(mode, bundle, args.connector_secret)

        write_section("Deployment Summary")
        if connector_id:
            write_success(f"Connector ID: {connector_id}")
            if environment_id:
                write_detail(f"Environment:  {environment_id}")

            redirect_urls: list[str] = []
            try:
                redirect_urls = get_connector_redirect_urls(connector_id)
                for redirect_url in redirect_urls:
                    write_success(f"Redirect URL: {redirect_url}")
            except Exception as error:  # noqa: BLE001
                write_detail(f"Could not derive redirect URL: {error}")

            if mode == "Install" and redirect_urls:
                write_section("Next Steps — Configure Ironclad OAuth")
                if _console:
                    _console.print(
                        "  The connector is deployed. Before users can authenticate, you must register\n"
                        "  the [bold]Redirect URL[/bold] above in your Ironclad application so that OAuth\n"
                        "  can complete successfully.\n"
                    )
                else:
                    print(
                        "\n  The connector is deployed. Before users can authenticate, you must register\n"
                        "  the Redirect URL above in your Ironclad application so that OAuth\n"
                        "  can complete successfully.\n"
                    )
                write_step("Register the Redirect URL in Ironclad")
                write_detail("Log in to your Ironclad account.")
                write_detail("Navigate to Company Settings → API tab.")
                write_detail("Open your existing application or click 'Create new app'.")
                write_detail("Under 'Redirect URIs', paste the Redirect URL shown above.")
                write_detail("Ensure Grant Type 'Authorization Code' is selected.")
                write_detail("Add all required scopes (workflows, workflow-schemas, users, companies, approvals, metadata, webhooks).")
                write_detail("Save the application and securely note your Client ID and Client Secret.")
        elif mode == "Update":
            write_success("Connector updated successfully.")
            write_detail("The redirect URL is unchanged after an update.")
        else:
            write_detail("Deployment completed but the connector ID could not be read back from the settings file.")

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
