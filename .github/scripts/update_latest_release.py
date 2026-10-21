import base64
import hashlib
import hmac
import json
import os
import re
import tempfile
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path, PurePosixPath
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen
import xml.etree.ElementTree as ET
from zoneinfo import ZoneInfo


README_PATH = Path("README.md")
UPDATE_MANIFEST_PATH = Path("mod-updates.json")
PACKAGE_MANIFEST_PATH = Path("mod-packages.json")
PACKAGE_SIGNATURE_PATH = Path("mod-packages.json.sig")
DETAILS_PATH = Path("mod-details.json")
DETAILS_SIG_PATH = Path("mod-details.json.sig")

MANIFEST_AUTHOR = "Loupito75"
MANIFEST_GUID_PREFIX = "loupito75."
README_TIMEZONE = ZoneInfo("America/Toronto")

TEST_MOD_TECHNICAL_NAME = "HospitalModUpdateTest"
TEST_MOD_DISPLAY_NAME = "HMUC Test Mod"

PACKAGE_FORMAT_VERSION = 1
PACKAGE_VALIDITY_DAYS = 90
PACKAGE_MAX_ENTRIES = 256
PACKAGE_MAX_BYTES = 536_870_912
PACKAGE_MAX_UNPACKED_BYTES = 1_073_741_824
ZIP_MAX_ENTRIES = 4096

SIGNATURE_FORMAT_VERSION = 1
SIGNATURE_ALGORITHM = "RSASSA-PKCS1-v1_5-SHA256"
SIGNING_SECRET_NAME = "HMUC_PACKAGE_SIGNING_KEY_B64"
RSA_KEY_BITS = 3072
RSA_SIGNATURE_BYTES = RSA_KEY_BITS // 8

SHA256_DIGEST_INFO_PREFIX = bytes.fromhex(
    "3031300d060960864801650304020105000420"
)


def normalize_name(value):
    return re.sub(r"[^a-z0-9]", "", value.lower())


def split_row(line):
    return [
        cell.strip()
        for cell in line.strip().strip("|").split("|")
    ]


def build_row(cells):
    return "| " + " | ".join(cells) + " |"


def get_mod_name_from_cell(cell):
    match = re.search(r"\[([^\]]+)\]", cell)

    if match:
        name = match.group(1)
    else:
        name = cell.strip()

    return re.sub(
        r"\s+\([A-Z0-9]+\)\s*$",
        "",
        name,
    ).strip()


def get_technical_mod_name_from_cell(cell):
    match = re.search(
        r"\]\(mods/Loupito75\.([A-Za-z0-9]+)/?\)",
        cell,
    )

    if match:
        return match.group(1)

    return None


def utc_text(value):
    return (
        value.astimezone(timezone.utc)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z")
    )


def format_readme_date(published_at):
    if not published_at:
        raise RuntimeError(
            "Release published_at is missing."
        )

    try:
        published = datetime.fromisoformat(
            published_at.replace("Z", "+00:00")
        )
    except ValueError as error:
        raise RuntimeError(
            "Invalid release published_at value: "
            + published_at
        ) from error

    if published.tzinfo is None:
        published = published.replace(
            tzinfo=timezone.utc
        )

    local_date = published.astimezone(
        README_TIMEZONE
    ).strftime("%Y-%m-%d")

    return local_date.replace(
        "-",
        "&#x2060;-&#x2060;",
    )


def github_headers(
    accept="application/vnd.github+json",
):
    headers = {
        "Accept": accept,
        "User-Agent":
            "Project-Hospital-Mods-release-metadata",
        "X-GitHub-Api-Version": "2022-11-28",
    }

    github_token = os.environ.get(
        "GITHUB_TOKEN",
        "",
    ).strip()

    if github_token:
        headers["Authorization"] = (
            "Bearer " + github_token
        )

    return headers


def fetch_release_by_tag(repository, tag):
    encoded_tag = quote(tag, safe="")

    api_url = (
        "https://api.github.com/repos/"
        + repository
        + "/releases/tags/"
        + encoded_tag
    )

    request = Request(
        api_url,
        headers=github_headers(),
        method="GET",
    )

    try:
        with urlopen(
            request,
            timeout=30,
        ) as response:
            release = json.load(response)

    except HTTPError as error:
        raise RuntimeError(
            "GitHub API returned HTTP "
            + str(error.code)
            + " while loading release tag: "
            + tag
        ) from error

    except URLError as error:
        raise RuntimeError(
            "Could not load release metadata "
            "from GitHub for tag: "
            + tag
        ) from error

    if not isinstance(release, dict):
        raise RuntimeError(
            "GitHub returned invalid release metadata "
            "for tag: "
            + tag
        )

    return release


def load_event():
    event_path = os.environ.get(
        "GITHUB_EVENT_PATH"
    )

    if not event_path:
        raise RuntimeError(
            "GITHUB_EVENT_PATH is not available."
        )

    with open(
        event_path,
        "r",
        encoding="utf-8",
    ) as file:
        event = json.load(file)

    if not isinstance(event, dict):
        raise RuntimeError(
            "GitHub event payload is invalid."
        )

    return event


def parse_release(release):
    tag = (
        release.get("tag_name") or ""
    ).strip()

    release_name = (
        release.get("name") or ""
    ).strip()

    release_url = (
        release.get("html_url") or ""
    ).strip()

    published_at = (
        release.get("published_at") or ""
    ).strip()

    if not tag:
        raise RuntimeError(
            "Release tag is missing."
        )

    if not release_url:
        raise RuntimeError(
            "Release URL is missing."
        )

    if not published_at:
        raise RuntimeError(
            "Release publication date is missing "
            "for tag: "
            + tag
        )

    match = re.fullmatch(
        r"(.+)-v(\d+\.\d+\.\d+)",
        tag,
    )

    if not match:
        raise RuntimeError(
            "Release tag must use the format "
            "ModName-vX.Y.Z: "
            + tag
        )

    technical_mod_name = match.group(1)
    version = match.group(2)

    return (
        technical_mod_name,
        version,
        release_name,
        release_url,
        published_at,
    )


def load_release(event):
    release = event.get("release")

    if not release:
        inputs = event.get("inputs") or {}

        tag = (
            inputs.get("tag") or ""
        ).strip()

        if not tag:
            raise RuntimeError(
                "No release tag was provided."
            )

        repository = os.environ.get(
            "GITHUB_REPOSITORY",
            "",
        ).strip()

        if not repository:
            raise RuntimeError(
                "GITHUB_REPOSITORY is not available."
            )

        release = fetch_release_by_tag(
            repository,
            tag,
        )

    if not isinstance(release, dict):
        raise RuntimeError(
            "GitHub release payload is invalid."
        )

    return release, parse_release(release)


def update_readme(
    technical_mod_name,
    version,
    release_url,
    published_at,
):
    release_date = format_readme_date(
        published_at
    )

    latest_release = (
        f"[{version}]({release_url})"
        f"<br>{release_date}"
    )

    target_name = normalize_name(
        technical_mod_name
    )

    text = README_PATH.read_text(
        encoding="utf-8"
    )

    lines = text.splitlines()

    mods_heading = None

    for index, line in enumerate(lines):
        if line.strip().lower() == "## mods":
            mods_heading = index
            break

    if mods_heading is None:
        raise RuntimeError(
            "Could not find the '## Mods' "
            "section in README.md."
        )

    section_end = len(lines)

    for index in range(
        mods_heading + 1,
        len(lines),
    ):
        if lines[index].startswith("## "):
            section_end = index
            break

    header_index = None

    for index in range(
        mods_heading + 1,
        section_end,
    ):
        if not lines[index].strip().startswith("|"):
            continue

        cells = split_row(lines[index])

        if (
            "Mod" in cells
            and "Latest release" in cells
        ):
            header_index = index
            break

    if header_index is None:
        raise RuntimeError(
            "Could not find the Mods table."
        )

    headers = split_row(
        lines[header_index]
    )

    mod_column = headers.index("Mod")
    latest_column = headers.index(
        "Latest release"
    )

    found = False
    display_name = ""

    for index in range(
        header_index + 2,
        section_end,
    ):
        line = lines[index]

        if not line.strip().startswith("|"):
            break

        cells = split_row(line)

        while len(cells) < len(headers):
            cells.append("")

        mod_cell = cells[mod_column]

        mod_name = get_mod_name_from_cell(
            mod_cell
        )

        readme_technical_name = (
            get_technical_mod_name_from_cell(
                mod_cell
            )
        )

        if readme_technical_name is not None:
            if (
                normalize_name(
                    readme_technical_name
                )
                != target_name
            ):
                continue

        elif (
            normalize_name(mod_name)
            != target_name
        ):
            continue

        display_name = mod_name

        cells[latest_column] = (
            latest_release
        )

        lines[index] = build_row(cells)
        found = True

        print(
            "Updated latest release for "
            + mod_name
            + " to "
            + latest_release
        )

        break

    if not found:
        raise RuntimeError(
            "Mod was not found in the README table: "
            + technical_mod_name
            + ". Add its complete README row, "
            "including description, "
            "First release date and links, "
            "before publishing the release."
        )

    new_text = "\n".join(lines) + "\n"

    if new_text == text:
        print(
            "README.md is already up to date."
        )
    else:
        README_PATH.write_text(
            new_text,
            encoding="utf-8",
        )

        print("README.md updated.")

    return display_name


def update_update_manifest(
    technical_mod_name,
    display_name,
    version,
):
    if not UPDATE_MANIFEST_PATH.exists():
        raise RuntimeError(
            "mod-updates.json was not found."
        )

    with UPDATE_MANIFEST_PATH.open(
        "r",
        encoding="utf-8",
    ) as file:
        manifest = json.load(file)

    if manifest.get("formatVersion") != 1:
        raise RuntimeError(
            "Unsupported mod-updates.json "
            "formatVersion: "
            + str(
                manifest.get("formatVersion")
            )
        )

    revision = manifest.get(
        "manifestRevision"
    )

    if (
        not isinstance(revision, int)
        or isinstance(revision, bool)
        or revision < 1
    ):
        raise RuntimeError(
            "mod-updates.json manifestRevision "
            "must be an integer >= 1."
        )

    mods = manifest.get("mods")

    if not isinstance(mods, list):
        raise RuntimeError(
            "mod-updates.json 'mods' "
            "must be an array."
        )

    guid = (
        MANIFEST_GUID_PREFIX
        + technical_mod_name
    )

    target_guid = guid.lower()

    matches = []

    for candidate in mods:
        if not isinstance(
            candidate,
            dict,
        ):
            raise RuntimeError(
                "mod-updates.json contains "
                "a non-object mod entry."
            )

        candidate_guid = candidate.get(
            "guid"
        )

        if (
            isinstance(
                candidate_guid,
                str,
            )
            and candidate_guid.lower()
                == target_guid
        ):
            matches.append(candidate)

    if len(matches) > 1:
        raise RuntimeError(
            "mod-updates.json contains "
            "duplicate entries for GUID: "
            + guid
        )

    manifest_changed = False

    if not matches:
        mods.append(
            {
                "guid": guid,
                "name": display_name,
                "author": MANIFEST_AUTHOR,
                "version": version,
            }
        )

        manifest_changed = True

        print(
            "Added mod-updates.json entry for "
            + display_name
            + " at version "
            + version
            + "."
        )

    else:
        entry = matches[0]
        current_version = entry.get(
            "version"
        )

        if current_version != version:
            entry["version"] = version
            manifest_changed = True

            print(
                "Updated mod-updates.json version for "
                + display_name
                + " from "
                + str(current_version)
                + " to "
                + version
                + "."
            )
        else:
            print(
                "mod-updates.json version for "
                + display_name
                + " is already "
                + version
                + "."
            )

    if not manifest_changed:
        return

    manifest["manifestRevision"] = (
        revision + 1
    )

    manifest["updatedUtc"] = utc_text(
        datetime.now(timezone.utc)
    )

    UPDATE_MANIFEST_PATH.write_text(
        json.dumps(
            manifest,
            indent=2,
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
    )

    print(
        "mod-updates.json updated. "
        "manifestRevision="
        + str(
            manifest["manifestRevision"]
        )
        + " | updatedUtc="
        + manifest["updatedUtc"]
    )


def find_release_asset(
    release,
    technical_mod_name,
    version,
):
    expected_name = (
        "Loupito75."
        + technical_mod_name
        + "-v"
        + version
        + ".zip"
    )

    assets = release.get("assets")

    if not isinstance(assets, list):
        raise RuntimeError(
            "Release assets are missing "
            "or invalid."
        )

    matches = [
        asset
        for asset in assets
        if isinstance(asset, dict)
        and (
            asset.get("name") or ""
        ).strip()
        == expected_name
    ]

    if len(matches) != 1:
        raise RuntimeError(
            "Expected exactly one release asset "
            "named "
            + expected_name
            + ", found "
            + str(len(matches))
            + "."
        )

    asset = matches[0]

    asset_size = asset.get("size")

    if (
        not isinstance(asset_size, int)
        or isinstance(asset_size, bool)
        or asset_size <= 0
        or asset_size
            > PACKAGE_MAX_BYTES
    ):
        raise RuntimeError(
            "Release asset has an invalid size: "
            + str(asset_size)
        )

    if not asset.get("url"):
        raise RuntimeError(
            "Release asset API URL is missing."
        )

    if not asset.get(
        "browser_download_url"
    ):
        raise RuntimeError(
            "Release asset browser_download_url "
            "is missing."
        )

    return asset


def download_release_asset(
    asset,
    destination,
):
    request = Request(
        asset["url"],
        headers=github_headers(
            "application/octet-stream"
        ),
        method="GET",
    )

    sha256 = hashlib.sha256()
    downloaded = 0

    try:
        with urlopen(
            request,
            timeout=60,
        ) as response:

            with destination.open(
                "wb"
            ) as output:

                while True:
                    chunk = response.read(
                        1024 * 1024
                    )

                    if not chunk:
                        break

                    downloaded += len(chunk)

                    if (
                        downloaded
                        > PACKAGE_MAX_BYTES
                    ):
                        raise RuntimeError(
                            "Release asset exceeded "
                            "HMUC package size limit."
                        )

                    sha256.update(chunk)
                    output.write(chunk)

    except HTTPError as error:
        raise RuntimeError(
            "GitHub API returned HTTP "
            + str(error.code)
            + " while downloading "
            "release asset."
        ) from error

    except URLError as error:
        raise RuntimeError(
            "Could not download release asset."
        ) from error

    expected_size = asset["size"]

    if downloaded != expected_size:
        raise RuntimeError(
            "Downloaded release asset size "
            "does not match GitHub metadata: "
            + str(downloaded)
            + " != "
            + str(expected_size)
        )

    return (
        sha256.hexdigest(),
        downloaded,
    )


def validate_zip_entry_name(name):
    normalized = name.replace(
        "\\",
        "/",
    )

    if (
        not normalized
        or normalized.startswith("/")
        or "\x00" in normalized
        or ":" in normalized
    ):
        raise RuntimeError(
            "ZIP contains an unsafe path: "
            + repr(name)
        )

    trimmed = normalized.rstrip("/")

    if not trimmed:
        raise RuntimeError(
            "ZIP contains an invalid root entry."
        )

    path = PurePosixPath(trimmed)

    if any(
        part in ("", ".", "..")
        for part in path.parts
    ):
        raise RuntimeError(
            "ZIP contains an unsafe path: "
            + repr(name)
        )

    return normalized


def is_zip_symlink(info):
    unix_mode = (
        info.external_attr >> 16
    ) & 0xFFFF

    return (
        unix_mode & 0o170000
    ) == 0o120000


def choose_config_file(
    install_directory,
    top_level_files,
):
    exact = (
        install_directory
        + ".Config.xml"
    )

    by_lower = {
        value.lower(): value
        for value in top_level_files
    }

    exact_match = by_lower.get(
        exact.lower()
    )

    if exact_match:
        return exact_match

    prefix = (
        install_directory + "."
    ).lower()

    candidates = sorted(
        value
        for value in top_level_files
        if value.lower().startswith(prefix)
        and value.lower().endswith(".xml")
    )

    if len(candidates) != 1:
        raise RuntimeError(
            "Could not determine the mod's exact "
            "configuration XML. Expected "
            + exact
            + " or exactly one top-level XML "
            "prefixed with "
            + install_directory
            + ". Found: "
            + (
                ", ".join(candidates)
                if candidates
                else "none"
            )
        )

    return candidates[0]


def inspect_release_zip(
    zip_path,
    technical_mod_name,
    display_name,
    version,
    download_url,
    sha256,
    package_bytes,
):
    install_directory = (
        "Loupito75."
        + technical_mod_name
    )

    archive_root = install_directory

    plugin_file = (
        install_directory
        + ".dll"
    )

    root_prefix = (
        archive_root
        + "/"
    )

    top_level_files = set()
    package_files = set()
    seen_paths = set()

    unpacked_bytes = 0

    try:
        with zipfile.ZipFile(
            zip_path,
            "r",
        ) as archive:

            infos = archive.infolist()

            if (
                len(infos) == 0
                or len(infos)
                    > ZIP_MAX_ENTRIES
            ):
                raise RuntimeError(
                    "Release ZIP contains "
                    "an invalid number of entries."
                )

            for info in infos:
                name = validate_zip_entry_name(
                    info.filename
                )

                normalized = (
                    name.rstrip("/")
                )

                path_key = normalized.lower()

                if path_key in seen_paths:
                    raise RuntimeError(
                        "Release ZIP contains "
                        "duplicate paths: "
                        + normalized
                    )

                seen_paths.add(path_key)

                if info.flag_bits & 0x1:
                    raise RuntimeError(
                        "Release ZIP contains "
                        "an encrypted entry: "
                        + normalized
                    )

                if info.compress_type not in (
                    zipfile.ZIP_STORED,
                    zipfile.ZIP_DEFLATED,
                ):
                    raise RuntimeError(
                        "Release ZIP uses an "
                        "unsupported compression method: "
                        + normalized
                    )

                if is_zip_symlink(info):
                    raise RuntimeError(
                        "Release ZIP contains "
                        "a symbolic link: "
                        + normalized
                    )

                if normalized == archive_root:
                    if not info.is_dir():
                        raise RuntimeError(
                            "Release ZIP root entry "
                            "must be a directory."
                        )

                    continue

                if not normalized.startswith(
                    root_prefix
                ):
                    raise RuntimeError(
                        "Release ZIP contains content "
                        "outside "
                        + archive_root
                        + ": "
                        + normalized
                    )

                relative = normalized[
                    len(root_prefix):
                ]

                if not relative:
                    continue

                parts = PurePosixPath(
                    relative
                ).parts

                if info.is_dir():
                    continue

                unpacked_bytes += (
                    info.file_size
                )

                if (
                    unpacked_bytes
                    > PACKAGE_MAX_UNPACKED_BYTES
                ):
                    raise RuntimeError(
                        "Release ZIP exceeds "
                        "HMUC unpacked size limit."
                    )

                package_files.add(
                    relative
                )

                if len(parts) == 1:
                    top_level_files.add(
                        parts[0]
                    )

    except zipfile.BadZipFile as error:
        raise RuntimeError(
            "Release asset is not "
            "a valid ZIP file."
        ) from error

    if plugin_file not in top_level_files:
        raise RuntimeError(
            "Release ZIP is missing "
            "the main plugin DLL: "
            + plugin_file
        )

    config_file = choose_config_file(
        install_directory,
        top_level_files,
    )

    runtime_files = sorted(
        (
            value
            for value in package_files
            if value.lower()
            != config_file.lower()
        ),
        key=str.lower,
    )

    # Ownership is intentionally file-by-file.
    # Never infer a mirror directory from ZIP structure:
    # a directory such as Audio/MyMusic may contain user files
    # that must survive a mod update.
    runtime_directories = []

    if plugin_file not in runtime_files:
        raise RuntimeError(
            "Main plugin DLL was not selected "
            "as a managed runtime file."
        )

    if unpacked_bytes <= 0:
        raise RuntimeError(
            "Release ZIP has no "
            "unpacked file content."
        )

    return {
        "guid":
            MANIFEST_GUID_PREFIX
            + technical_mod_name,

        "name":
            display_name,

        "version":
            version,

        "downloadUrl":
            download_url,

        "sha256":
            sha256,

        "packageBytes":
            package_bytes,

        "maximumUnpackedBytes":
            unpacked_bytes,

        "installDirectory":
            install_directory,

        "archiveRoot":
            archive_root,

        "pluginFile":
            plugin_file,

        "configFile":
            config_file,

        "runtimeDirectories":
            runtime_directories,

        "runtimeFiles":
            runtime_files,

        # Deletions must always be explicit.
        # The workflow never infers them.
        "removeFiles": [],
    }


def load_package_manifest(
    create_if_missing,
):
    if not PACKAGE_MANIFEST_PATH.exists():
        if not create_if_missing:
            return None

        return {
            "formatVersion":
                PACKAGE_FORMAT_VERSION,

            "manifestRevision":
                0,

            "updatedUtc":
                "",

            "expiresUtc":
                "",

            "packages":
                [],
        }

    with PACKAGE_MANIFEST_PATH.open(
        "r",
        encoding="utf-8",
    ) as file:
        manifest = json.load(file)

    if not isinstance(manifest, dict):
        raise RuntimeError(
            "mod-packages.json root "
            "must be an object."
        )

    allowed_keys = {
        "formatVersion",
        "manifestRevision",
        "updatedUtc",
        "expiresUtc",
        "packages",
    }

    unknown_keys = (
        set(manifest)
        - allowed_keys
    )

    if unknown_keys:
        raise RuntimeError(
            "mod-packages.json contains "
            "unknown root fields: "
            + ", ".join(
                sorted(unknown_keys)
            )
        )

    if (
        manifest.get("formatVersion")
        != PACKAGE_FORMAT_VERSION
    ):
        raise RuntimeError(
            "Unsupported mod-packages.json "
            "formatVersion: "
            + str(
                manifest.get(
                    "formatVersion"
                )
            )
        )

    revision = manifest.get(
        "manifestRevision"
    )

    if (
        not isinstance(revision, int)
        or isinstance(revision, bool)
        or revision < 1
    ):
        raise RuntimeError(
            "mod-packages.json manifestRevision "
            "must be an integer >= 1."
        )

    packages = manifest.get(
        "packages"
    )

    if not isinstance(packages, list):
        raise RuntimeError(
            "mod-packages.json 'packages' "
            "must be an array."
        )

    if len(packages) > PACKAGE_MAX_ENTRIES:
        raise RuntimeError(
            "mod-packages.json contains "
            "too many package entries."
        )

    guids = set()

    for package in packages:
        if not isinstance(
            package,
            dict,
        ):
            raise RuntimeError(
                "mod-packages.json contains "
                "a non-object package entry."
            )

        guid = package.get("guid")

        if (
            not isinstance(guid, str)
            or not guid
        ):
            raise RuntimeError(
                "mod-packages.json contains "
                "a package without a GUID."
            )

        key = guid.lower()

        if key in guids:
            raise RuntimeError(
                "mod-packages.json contains "
                "duplicate GUID: "
                + guid
            )

        guids.add(key)

    return manifest


def version_tuple(value):
    match = re.fullmatch(
        r"(\d+)\.(\d+)\.(\d+)",
        value or "",
    )

    if not match:
        raise RuntimeError(
            "Invalid x.x.x version: "
            + str(value)
        )

    return tuple(
        int(part)
        for part in match.groups()
    )


def upsert_package(
    manifest,
    package,
):
    packages = manifest["packages"]
    target_guid = package["guid"].lower()

    match_index = None

    for index, existing in enumerate(
        packages
    ):
        if (
            existing.get(
                "guid",
                "",
            ).lower()
            == target_guid
        ):
            match_index = index
            break

    if match_index is None:
        if len(packages) >= PACKAGE_MAX_ENTRIES:
            raise RuntimeError(
                "mod-packages.json has reached "
                "the HMUC package-entry limit."
            )

        packages.append(package)

        print(
            "Added mod-packages.json entry for "
            + package["name"]
            + " "
            + package["version"]
            + "."
        )

        return

    existing = packages[match_index]

    old_version = existing.get(
        "version"
    )

    if old_version:
        if (
            version_tuple(
                package["version"]
            )
            < version_tuple(
                old_version
            )
        ):
            raise RuntimeError(
                "Refusing package version downgrade "
                "for "
                + package["guid"]
                + ": "
                + old_version
                + " -> "
                + package["version"]
            )

        if (
            old_version
            == package["version"]
            and existing != package
        ):
            raise RuntimeError(
                "Package metadata changed for "
                "an already published version "
                + package["version"]
                + ". Publish a new version instead "
                "of replacing an existing asset."
            )

    if existing == package:
        print(
            "mod-packages.json entry "
            "is already current for "
            + package["name"]
            + " "
            + package["version"]
            + "."
        )

        return

    packages[match_index] = package

    print(
        "Updated mod-packages.json entry for "
        + package["name"]
        + " from "
        + str(old_version)
        + " to "
        + package["version"]
        + "."
    )


def get_required_signing_secret():
    encoded = os.environ.get(
        SIGNING_SECRET_NAME,
        "",
    ).strip()

    if not encoded:
        raise RuntimeError(
            SIGNING_SECRET_NAME
            + " GitHub Actions secret "
            "is missing."
        )

    try:
        return base64.b64decode(
            encoded,
            validate=True,
        )

    except Exception as error:
        raise RuntimeError(
            SIGNING_SECRET_NAME
            + " is not valid Base64."
        ) from error


def rsa_int(root, name):
    value = root.findtext(name)

    if not value:
        raise RuntimeError(
            "HMUC signing key is missing "
            "RSA field: "
            + name
        )

    try:
        raw = base64.b64decode(
            value.strip(),
            validate=True,
        )

    except Exception as error:
        raise RuntimeError(
            "HMUC signing key contains "
            "invalid Base64 in: "
            + name
        ) from error

    if not raw:
        raise RuntimeError(
            "HMUC signing key field "
            "is empty: "
            + name
        )

    return int.from_bytes(
        raw,
        "big",
    )


def load_signing_key():
    xml_bytes = (
        get_required_signing_secret()
    )

    if b"<!DOCTYPE" in xml_bytes.upper():
        raise RuntimeError(
            "HMUC signing key must not contain a DTD."
        )

    try:
        root = ET.fromstring(
            xml_bytes
        )

    except ET.ParseError as error:
        raise RuntimeError(
            "HMUC signing key secret "
            "is not valid XML."
        ) from error

    if root.tag != "HMUCSigningKey":
        raise RuntimeError(
            "HMUC signing key has "
            "an unexpected root element."
        )

    if (
        root.attrib.get(
            "formatVersion"
        )
        != "1"
    ):
        raise RuntimeError(
            "HMUC signing key "
            "formatVersion is invalid."
        )

    if (
        root.attrib.get("algorithm")
        != SIGNATURE_ALGORITHM
    ):
        raise RuntimeError(
            "HMUC signing key "
            "algorithm is invalid."
        )

    key_id = (
        root.attrib.get("keyId")
        or ""
    ).strip()

    if not re.fullmatch(
        r"[A-Za-z0-9._-]{3,128}",
        key_id,
    ):
        raise RuntimeError(
            "HMUC signing key ID is invalid."
        )

    modulus = rsa_int(
        root,
        "Modulus",
    )

    exponent = rsa_int(
        root,
        "Exponent",
    )

    private_exponent = rsa_int(
        root,
        "D",
    )

    p = rsa_int(root, "P")
    q = rsa_int(root, "Q")

    # Require all private RSA fields generated by hmucsign,
    # even though the standard-library signer only needs n and d.
    rsa_int(root, "DP")
    rsa_int(root, "DQ")
    rsa_int(root, "InverseQ")

    if modulus.bit_length() != RSA_KEY_BITS:
        raise RuntimeError(
            "HMUC signing key must be "
            "exactly RSA-3072."
        )

    if p * q != modulus:
        raise RuntimeError(
            "HMUC signing key RSA parameters "
            "are inconsistent."
        )

    if (
        exponent <= 1
        or exponent % 2 == 0
        or private_exponent <= 1
        or private_exponent >= modulus
    ):
        raise RuntimeError(
            "HMUC signing key RSA exponents "
            "are invalid."
        )

    return {
        "keyId": key_id,
        "n": modulus,
        "e": exponent,
        "d": private_exponent,
    }


def build_pkcs1_v1_5_sha256_block(
    manifest_bytes,
    modulus_bytes,
):
    digest = hashlib.sha256(
        manifest_bytes
    ).digest()

    digest_info = (
        SHA256_DIGEST_INFO_PREFIX
        + digest
    )

    padding_length = (
        modulus_bytes
        - len(digest_info)
        - 3
    )

    if padding_length < 8:
        raise RuntimeError(
            "RSA key is too small for "
            "PKCS#1 v1.5 SHA-256 signing."
        )

    return (
        b"\x00\x01"
        + (b"\xff" * padding_length)
        + b"\x00"
        + digest_info
    )


def sign_manifest_bytes(
    manifest_bytes,
    key,
):
    modulus_bytes = (
        key["n"].bit_length() + 7
    ) // 8

    if modulus_bytes != RSA_SIGNATURE_BYTES:
        raise RuntimeError(
            "HMUC signing key has "
            "an unexpected modulus size."
        )

    encoded = (
        build_pkcs1_v1_5_sha256_block(
            manifest_bytes,
            modulus_bytes,
        )
    )

    encoded_integer = int.from_bytes(
        encoded,
        "big",
    )

    signature_integer = pow(
        encoded_integer,
        key["d"],
        key["n"],
    )

    signature = signature_integer.to_bytes(
        modulus_bytes,
        "big",
    )

    recovered_integer = pow(
        signature_integer,
        key["e"],
        key["n"],
    )

    recovered = recovered_integer.to_bytes(
        modulus_bytes,
        "big",
    )

    if not hmac.compare_digest(
        recovered,
        encoded,
    ):
        raise RuntimeError(
            "HMUC RSA signature self-verification failed."
        )

    return signature


def sign_package_manifest():
    manifest_bytes = (
        PACKAGE_MANIFEST_PATH.read_bytes()
    )

    key = load_signing_key()

    signature = sign_manifest_bytes(
        manifest_bytes,
        key,
    )

    envelope = {
        "formatVersion":
            SIGNATURE_FORMAT_VERSION,

        "keyId":
            key["keyId"],

        "algorithm":
            SIGNATURE_ALGORITHM,

        "signature":
            base64.b64encode(
                signature
            ).decode("ascii"),
    }

    PACKAGE_SIGNATURE_PATH.write_text(
        json.dumps(
            envelope,
            indent=2,
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
    )

    print(
        "mod-packages.json signed "
        "and locally verified with key "
        + key["keyId"]
        + "."
    )


def seal_package_manifest(manifest):
    revision = manifest.get(
        "manifestRevision",
        0,
    )

    if (
        not isinstance(revision, int)
        or isinstance(revision, bool)
        or revision < 0
    ):
        raise RuntimeError(
            "mod-packages.json manifestRevision "
            "must be an integer >= 0."
        )

    packages = manifest.get("packages")

    if not isinstance(packages, list):
        raise RuntimeError(
            "mod-packages.json 'packages' "
            "must be an array."
        )

    if len(packages) > PACKAGE_MAX_ENTRIES:
        raise RuntimeError(
            "mod-packages.json contains "
            "too many package entries."
        )

    now = datetime.now(timezone.utc)

    manifest["manifestRevision"] = (
        revision + 1
    )

    manifest["updatedUtc"] = utc_text(
        now
    )

    manifest["expiresUtc"] = utc_text(
        now
        + timedelta(
            days=PACKAGE_VALIDITY_DAYS
        )
    )

    manifest["packages"] = sorted(
        packages,
        key=lambda item:
            item["guid"].lower(),
    )

    PACKAGE_MANIFEST_PATH.write_text(
        json.dumps(
            manifest,
            indent=2,
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
    )

    sign_package_manifest()

    print(
        "mod-packages.json sealed. "
        "manifestRevision="
        + str(
            manifest["manifestRevision"]
        )
        + " | expiresUtc="
        + manifest["expiresUtc"]
    )


def read_public_package_targets():
    text = README_PATH.read_text(
        encoding="utf-8"
    )

    lines = text.splitlines()

    mods_heading = None

    for index, line in enumerate(lines):
        if line.strip().lower() == "## mods":
            mods_heading = index
            break

    if mods_heading is None:
        raise RuntimeError(
            "Could not find the '## Mods' "
            "section in README.md."
        )

    targets = []
    seen = set()

    for line in lines[
        mods_heading + 1:
    ]:
        if line.startswith("## "):
            break

        if not line.strip().startswith("|"):
            continue

        cells = split_row(line)

        if len(cells) < 2:
            continue

        technical_mod_name = (
            get_technical_mod_name_from_cell(
                cells[0]
            )
        )

        if not technical_mod_name:
            continue

        display_name = get_mod_name_from_cell(
            cells[0]
        )

        latest_cell = cells[1]

        match = re.search(
            r"\[(\d+\.\d+\.\d+)\]"
            r"\("
            r"https://github\.com/"
            r"Loupito75/Project-Hospital-Mods/"
            r"releases/tag/([^)]+)"
            r"\)",
            latest_cell,
        )

        if not match:
            raise RuntimeError(
                "Could not determine the latest "
                "public release for "
                + technical_mod_name
                + " from README.md."
            )

        version = match.group(1)
        tag = match.group(2)

        expected_tag = (
            technical_mod_name
            + "-v"
            + version
        )

        if tag not in (
            expected_tag,
            expected_tag + "-republished",
        ):
            raise RuntimeError(
                "Unexpected release tag for "
                + technical_mod_name
                + ": "
                + tag
            )

        key = technical_mod_name.lower()

        if key in seen:
            raise RuntimeError(
                "README.md contains duplicate "
                "mod entries for "
                + technical_mod_name
            )

        seen.add(key)

        targets.append(
            {
                "technicalName":
                    technical_mod_name,

                "displayName":
                    display_name,

                "version":
                    version,

                "tag":
                    tag,
            }
        )

    if not targets:
        raise RuntimeError(
            "No public mods were found "
            "in README.md."
        )

    return targets


def rebuild_package_catalog():
    repository = os.environ.get(
        "GITHUB_REPOSITORY",
        "",
    ).strip()

    if not repository:
        raise RuntimeError(
            "GITHUB_REPOSITORY is not available."
        )

    existing_manifest = (
        load_package_manifest(
            create_if_missing=True
        )
    )

    existing_by_guid = {}

    for package in existing_manifest[
        "packages"
    ]:
        guid = (
            package.get("guid")
            or ""
        ).lower()

        if guid:
            existing_by_guid[guid] = package

    targets = read_public_package_targets()

    rebuilt_packages = []

    for target in targets:
        technical_mod_name = (
            target["technicalName"]
        )

        display_name = (
            target["displayName"]
        )

        version = target["version"]
        tag = target["tag"]

        print(
            "Rebuilding package metadata for "
            + display_name
            + " "
            + version
            + " from "
            + tag
        )

        release = fetch_release_by_tag(
            repository,
            tag,
        )

        if release.get("draft"):
            raise RuntimeError(
                "README.md points to a draft "
                "release: "
                + tag
            )

        if release.get("prerelease"):
            raise RuntimeError(
                "README.md points to a prerelease: "
                + tag
            )

        asset = find_release_asset(
            release,
            technical_mod_name,
            version,
        )

        with tempfile.TemporaryDirectory(
            prefix="hmuc-rebuild-"
        ) as temporary_directory:

            zip_path = (
                Path(temporary_directory)
                / asset["name"]
            )

            (
                sha256,
                package_bytes,
            ) = download_release_asset(
                asset,
                zip_path,
            )

            package = inspect_release_zip(
                zip_path=zip_path,
                technical_mod_name=
                    technical_mod_name,
                display_name=
                    display_name,
                version=
                    version,
                download_url=
                    asset[
                        "browser_download_url"
                    ],
                sha256=
                    sha256,
                package_bytes=
                    package_bytes,
            )

        existing = existing_by_guid.get(
            package["guid"].lower()
        )

        if existing is not None:
            # Deletions are explicit metadata and
            # cannot be reconstructed from a ZIP.
            package["removeFiles"] = list(
                existing.get(
                    "removeFiles",
                    [],
                )
            )

            if (
                existing.get("version")
                == package["version"]
                and existing != package
            ):
                raise RuntimeError(
                    "Rebuild detected changed "
                    "metadata for already published "
                    + package["guid"]
                    + " "
                    + package["version"]
                    + ". Publish a new version "
                    "instead of replacing an "
                    "existing release asset."
                )

        rebuilt_packages.append(package)

    # Keep the HMUC test package if it already
    # exists. It is intentionally not listed
    # in the public README Mods table.
    test_guid = (
        MANIFEST_GUID_PREFIX
        + TEST_MOD_TECHNICAL_NAME
    ).lower()

    existing_test = existing_by_guid.get(
        test_guid
    )

    if existing_test is not None:
        rebuilt_packages.append(
            existing_test
        )

    rebuilt_manifest = {
        "formatVersion":
            PACKAGE_FORMAT_VERSION,

        # seal_package_manifest increments
        # this exactly once.
        "manifestRevision":
            existing_manifest.get(
                "manifestRevision",
                0,
            ),

        "updatedUtc":
            existing_manifest.get(
                "updatedUtc",
                "",
            ),

        "expiresUtc":
            existing_manifest.get(
                "expiresUtc",
                "",
            ),

        "packages":
            rebuilt_packages,
    }

    seal_package_manifest(
        rebuilt_manifest
    )
    rebuild_mod_details(targets,
        {p["guid"].lower() for p in rebuilt_packages})

    print(
        "Full HMUC package catalog rebuild "
        "completed with "
        + str(len(rebuilt_packages))
        + " package entries."
    )


def process_release(event):
    release, parsed = load_release(
        event
    )

    (
        technical_mod_name,
        version,
        release_name,
        release_url,
        published_at,
    ) = parsed

    print(
        "Processing release "
        + technical_mod_name
        + " "
        + version
        + (
            " (" + release_name + ")"
            if release_name
            else ""
        )
    )

    if (
        technical_mod_name
        == TEST_MOD_TECHNICAL_NAME
    ):
        display_name = (
            TEST_MOD_DISPLAY_NAME
        )

        print(
            "HMUC test mod detected: "
            "README table update skipped."
        )

    else:
        display_name = update_readme(
            technical_mod_name,
            version,
            release_url,
            published_at,
        )

    update_update_manifest(
        technical_mod_name,
        display_name,
        version,
    )

    asset = find_release_asset(
        release,
        technical_mod_name,
        version,
    )

    with tempfile.TemporaryDirectory(
        prefix="hmuc-release-"
    ) as temporary_directory:

        zip_path = (
            Path(temporary_directory)
            / asset["name"]
        )

        (
            sha256,
            package_bytes,
        ) = download_release_asset(
            asset,
            zip_path,
        )

        package = inspect_release_zip(
            zip_path=zip_path,
            technical_mod_name=
                technical_mod_name,
            display_name=display_name,
            version=version,
            download_url=
                asset["browser_download_url"],
            sha256=sha256,
            package_bytes=
                package_bytes,
        )

    package_manifest = (
        load_package_manifest(
            create_if_missing=True
        )
    )

    upsert_package(
        package_manifest,
        package,
    )

    seal_package_manifest(
        package_manifest
    )
    publish_mod_details(technical_mod_name, version)


def refresh_package_catalog():
    manifest = load_package_manifest(
        create_if_missing=False
    )

    if manifest is None:
        print(
            "mod-packages.json does not "
            "exist yet; scheduled catalog "
            "refresh skipped."
        )

        return

    print(
        "Refreshing signed package catalog "
        "validity without changing package entries."
    )

    seal_package_manifest(
        manifest
    )
    refresh_mod_details()


def get_public_mod_text(technical, version):
    # The public repository mirrors the sources shipped in its DLL releases.
    # For descriptions the SDK compiles <Description> into the DLL. The
    # version-specific PluginWhatsNew string is a C# const in Plugin.cs.
    directory = Path("mods") / ("Loupito75." + technical)
    projects = list(directory.glob("*.csproj"))
    plugin = directory / "Plugin.cs"
    if len(projects) != 1 or not plugin.exists():
        return "", ""
    xml = projects[0].read_bytes()
    if len(xml) > 512000 or b"<!DOCTYPE" in xml.upper():
        raise RuntimeError("Unsafe source project metadata: " + technical)
    root = ET.fromstring(xml)
    node = root.find(".//Description")
    description = " ".join(
        (node.text if node is not None and node.text else "").split()
    )
    source = plugin.read_text(encoding="utf-8")
    match = re.search(
        r'\bconst\s+string\s+PluginVersion\s*=\s*"([^"]+)"', source
    )
    if match is None or match.group(1) != version:
        print("Skipping DLL text: public source version differs for "
              + technical + " " + version)
        return "", ""
    match = re.search(
        r'\bconst\s+string\s+PluginWhatsNew\s*=\s*"((?:\\.|[^"\\])*)"\s*;',
        source,
    )
    whats_new = ""
    if match:
        try:
            whats_new = json.loads('"' + match.group(1) + '"').strip()
        except ValueError as exc:
            raise RuntimeError(
                "Invalid PluginWhatsNew literal: " + technical
            ) from exc
    for name, text, maximum in (
        ("Description", description, 700),
        ("PluginWhatsNew", whats_new, 900),
    ):
        if len(text) > maximum or "<" in text or ">" in text:
            raise RuntimeError("Invalid " + name + " for " + technical)
        if text.count("\n") >= 8 or any(
            ord(c) < 32 and c != "\n" for c in text
        ):
            raise RuntimeError("Invalid " + name + " for " + technical)
    return description, whats_new


def load_mod_details():
    if not DETAILS_PATH.exists():
        return {
            "formatVersion": 1,
            "manifestRevision": 0,
            "updatedUtc": "",
            "expiresUtc": "",
            "mods": [],
        }
    value = json.loads(DETAILS_PATH.read_text(encoding="utf-8"))
    # Forward compatibility: additional signed root keys are retained.
    required = {"formatVersion", "manifestRevision", "updatedUtc",
                "expiresUtc", "mods"}
    if (not isinstance(value, dict) or
            not required.issubset(value) or
            value["formatVersion"] != 1 or
            not isinstance(value["mods"], list)):
        raise RuntimeError("Invalid mod-details.json structure")
    return value


def mod_details_entry(technical, version):
    description, whats_new = get_public_mod_text(technical, version)
    return {
        "guid": MANIFEST_GUID_PREFIX + technical,
        "version": version,
        "description": description,
        "whatsNew": whats_new,
    }


def seal_mod_details(manifest):
    revision = manifest["manifestRevision"]
    if (type(revision) is not int or revision < 0 or
            not isinstance(manifest["mods"], list) or
            len(manifest["mods"]) > PACKAGE_MAX_ENTRIES):
        raise RuntimeError("Invalid mod details revision/entries")
    seen = set()
    for item in manifest["mods"]:
        if not isinstance(item, dict) or not {
                "guid", "version"}.issubset(item):
            raise RuntimeError("Missing required mod details identity")
        # Extra fields are signed and retained unchanged, even if the
        # current HMUC does not understand them. Descriptions are optional.
        key = item["guid"].lower()
        if key in seen:
            raise RuntimeError("Duplicate mod details GUID")
        seen.add(key)
    now = datetime.now(timezone.utc)
    manifest["manifestRevision"] = revision + 1
    manifest["updatedUtc"] = utc_text(now)
    manifest["expiresUtc"] = utc_text(
        now + timedelta(days=PACKAGE_VALIDITY_DAYS)
    )
    manifest["mods"].sort(key=lambda x: x["guid"].lower())
    raw = (json.dumps(
        manifest, indent=2, ensure_ascii=False
    ) + "\n").encode("utf-8")
    # The client applies a bounded 256 KiB JSON limit. Check before
    # signing, including future extension fields preserved by this tool.
    if len(raw) > 262144:
        raise RuntimeError("mod-details.json exceeds the HMUC 256 KiB limit")
    DETAILS_PATH.write_bytes(raw)
    key = load_signing_key()
    signature = sign_manifest_bytes(raw, key)
    envelope = {
        "formatVersion": SIGNATURE_FORMAT_VERSION,
        "keyId": key["keyId"],
        "algorithm": SIGNATURE_ALGORITHM,
        "signature": base64.b64encode(signature).decode("ascii"),
    }
    DETAILS_SIG_PATH.write_text(
        json.dumps(envelope, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print("Signed mod-details.json revision "
          + str(manifest["manifestRevision"]))


def publish_mod_details(technical, version):
    manifest = load_mod_details()
    updated = mod_details_entry(technical, version)
    previous = next((
        x for x in manifest["mods"]
        if x["guid"].lower() == updated["guid"].lower()
    ), None)
    if previous is None:
        manifest["mods"].append(updated)
    elif previous["version"] == version and any(
            previous.get(k, "") != updated[k]
            for k in ("guid", "version", "description", "whatsNew")
    ):
        raise RuntimeError(
            "Refusing to change known metadata of an already published DLL: "
            + updated["guid"] + " " + version
        )
    else:
        # Preserve future extension fields that this publisher does not
        # know how to generate yet. Version-specific fields require their
        # own publisher support when they are introduced.
        previous.update(updated)
    seal_mod_details(manifest)


def rebuild_mod_details(targets, package_guids):
    manifest = load_mod_details()
    old = {x["guid"].lower(): x for x in manifest["mods"]}
    result = []
    for target in targets:
        entry = mod_details_entry(
            target["technicalName"], target["version"]
        )
        key = entry["guid"].lower()
        if key not in package_guids:
            continue
        prior = old.get(key)
        if (prior is not None):
            if (prior["version"] == entry["version"] and any(
                    prior.get(k, "") != entry[k]
                    for k in ("guid", "version", "description", "whatsNew")
            )):
                raise RuntimeError("Published DLL metadata changed: " + key)
            # Preserve fields introduced by later catalog extensions.
            # The known, version-bound text is always freshly generated.
            merged = dict(prior)
            merged.update(entry)
            entry = merged
        result.append(entry)
    test_guid = (MANIFEST_GUID_PREFIX
                 + TEST_MOD_TECHNICAL_NAME).lower()
    if test_guid in old and test_guid in package_guids:
        result.append(old[test_guid])
    manifest["mods"] = result
    seal_mod_details(manifest)


def refresh_mod_details():
    if DETAILS_PATH.exists():
        seal_mod_details(load_mod_details())


def main():
    event = load_event()

    event_name = os.environ.get(
        "GITHUB_EVENT_NAME",
        "",
    ).strip()

    if event_name == "schedule":
        refresh_package_catalog()
        return

    if event_name == "workflow_dispatch":
        inputs = event.get(
            "inputs"
        ) or {}

        mode = (
            inputs.get("mode")
            or "single-release"
        ).strip()

        if (
            mode
            == "rebuild-package-catalog"
        ):
            rebuild_package_catalog()
            return

        if mode != "single-release":
            raise RuntimeError(
                "Unsupported manual mode: "
                + mode
            )

    process_release(event)


if __name__ == "__main__":
    main()
