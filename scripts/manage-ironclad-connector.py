#!/usr/bin/env python3
"""Interactive cross-platform installer for the Ironclad CLM custom connector."""

from __future__ import annotations

import argparse
import importlib
import json
import shutil
import site
import subprocess
import sys
import tempfile
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


def write_section(message: str) -> None:
    print(f"\n== {message} ==")


def write_step(message: str) -> None:
    print(f"[Step] {message}")


def write_detail(message: str) -> None:
    print(f"  - {message}")


def run_python_module(arguments: list[str], *, interactive: bool = False) -> list[str]:
    command = [sys.executable, *arguments]
    if interactive:
        completed = subprocess.run(command, check=False)
        if completed.returncode != 0:
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


def add_user_site_packages() -> None:
    user_site = site.getusersitepackages()
    if user_site and user_site not in sys.path:
        sys.path.append(user_site)


def ensure_python_package(module_name: str, package_name: Optional[str] = None) -> Any:
    package_name = package_name or module_name
    try:
        return importlib.import_module(module_name)
    except ImportError:
        write_detail(f"Installing Python package '{package_name}'.")
        ensure_pip_available()
        run_python_module(["-m", "pip", "install", "--user", "--upgrade", package_name])
        importlib.invalidate_caches()
        add_user_site_packages()
        return importlib.import_module(module_name)


def ensure_paconn_installed() -> None:
    write_step("Checking for paconn.")
    write_detail(f"Using Python interpreter '{sys.executable}'.")

    try:
        run_python_module(["-m", "paconn", "--version"])
    except RuntimeError:
        write_detail("paconn is not installed. Installing it for the current user.")
        ensure_pip_available()
        run_python_module(["-m", "pip", "install", "--user", "--upgrade", "paconn"])

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


def ensure_questionary() -> Any:
    write_step("Checking the interactive console UI package used for the action and environment pickers.")
    return ensure_python_package("questionary")


def select_action(questionary: Any) -> str:
    write_step("Asking whether you want to install a new connector or update an existing one.")
    selection = questionary.select(
        "Select the connector action to run",
        choices=[
            questionary.Choice(
                title="Install - Create a new Ironclad CLM custom connector in one or more environments.",
                value="Install",
            ),
            questionary.Choice(
                title="Update - Update an existing Ironclad CLM custom connector with the latest GitHub payload.",
                value="Update",
            ),
        ],
    ).ask()
    if not selection:
        raise RuntimeError("No action was selected. The installer cannot continue.")
    return str(selection)


def select_environments(questionary: Any, environments: list[EnvironmentRecord]) -> list[EnvironmentRecord]:
    write_step("Presenting the environment picker. Use Space to select multiple environments, then press Enter.")
    selection = questionary.checkbox(
        "Select the environments for the Ironclad CLM connector",
        choices=[
            questionary.Choice(
                title=" | ".join(
                    filter(
                        None,
                        [
                            environment.display_name,
                            environment.environment_id,
                            environment.environment_type,
                            environment.location,
                            environment.source,
                        ],
                    )
                ),
                value=environment,
            )
            for environment in environments
        ],
        validate=lambda selected: True if selected else "Select at least one environment.",
    ).ask()
    if not selection:
        raise RuntimeError("No environments were selected. The installer cannot continue.")
    return list(selection)


def select_connector_registration(
    questionary: Any,
    environment_id: str,
    connectors: list[ConnectorRecord],
) -> ConnectorRecord:
    write_step(
        f"More than one matching connector was found in environment '{environment_id}'. Asking you to pick the one to update."
    )
    selection = questionary.select(
        f"Select the connector to update in {environment_id}",
        choices=[
            questionary.Choice(
                title=" | ".join(filter(None, [connector.display_name, connector.connector_id, connector.created_by])),
                value=connector,
            )
            for connector in connectors
        ],
    ).ask()
    if selection is None:
        raise RuntimeError(f"No connector was selected for environment '{environment_id}'.")
    return selection


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
    with urllib.request.urlopen(request, timeout=60) as response:
        archive_path.write_bytes(response.read())

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
    questionary: Any,
    token: dict[str, str],
    environment_id: str,
    bundle: BundlePaths,
    connector_name: str,
    state_root: Path,
) -> ResolvedConnector:
    settings_file_path = get_settings_file_path(state_root, environment_id)
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

    selected_connector = matches[0] if len(matches) == 1 else select_connector_registration(questionary, environment_id, matches)
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
        default=str(Path.home() / ".ironcladclm"),
        help="Directory used to store environment-specific deployment settings.",
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
        show_intro()
        ensure_paconn_installed()
        questionary = ensure_questionary()
        invoke_paconn_login()

        token = get_paconn_access_token()
        mode = select_action(questionary)
        environments = get_accessible_environments(token)
        selected_environments = select_environments(questionary, environments)
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
                        questionary,
                        token,
                        environment_id,
                        bundle,
                        args.connector_name,
                        state_root,
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
                if redirect_urls:
                    for redirect_url in redirect_urls:
                        print(f"  Redirect URL: {redirect_url}")
                else:
                    print("  Redirect URL: No redirect URL could be derived automatically.")
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
                print(f"  Deployment failed for environment {environment_id}: {error}")

        write_section("Deployment summary")
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
        print("\nInstaller cancelled by user.")
        return 1
    except Exception as error:  # noqa: BLE001
        print(f"\nInstaller failed: {error}")
        return 1
    finally:
        remove_temporary_bundle(bundle)


if __name__ == "__main__":
    raise SystemExit(main())
