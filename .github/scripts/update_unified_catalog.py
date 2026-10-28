#!/usr/bin/env python3
"""
Build and sign the unified HMUC catalog.

This script intentionally reuses the existing public release tooling in
.github/scripts/update_latest_release.py for release parsing, ZIP inspection,
SHA-256 calculation, README updates and RSA signing.

Manual source files:
  catalog/source/catalog-control.json
  catalog/source/news.json

Generated modern files:
  catalog/mod-catalog.json
  catalog/mod-catalog.json.sig

Legacy behavior:
  unifiedCatalogActive=false:
    existing legacy workflow remains authoritative; this script mirrors it into
    the unified catalog for testing.

  unifiedCatalogActive=true, before legacyDeleteAfterUtc:
    legacy files are reduced to HMUC only and mod-news.json contains one
    migration notice.

  at/after legacyDeleteAfterUtc:
    mod-updates.json, mod-packages.json(.sig) and mod-details.json(.sig) are
    deleted. mod-news.json remains as the migration/manual-download notice.
"""

import base64
import copy
import importlib.util
import json
import os
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]

CONTROL_PATH = ROOT / "catalog" / "source" / "catalog-control.json"
NEWS_SOURCE_PATH = ROOT / "catalog" / "source" / "news.json"
CATALOG_PATH = ROOT / "catalog" / "mod-catalog.json"
CATALOG_SIGNATURE_PATH = ROOT / "catalog" / "mod-catalog.json.sig"

UPDATE_MANIFEST_PATH = ROOT / "mod-updates.json"
PACKAGE_MANIFEST_PATH = ROOT / "mod-packages.json"
PACKAGE_SIGNATURE_PATH = ROOT / "mod-packages.json.sig"
DETAILS_PATH = ROOT / "mod-details.json"
DETAILS_SIGNATURE_PATH = ROOT / "mod-details.json.sig"
NEWS_LEGACY_PATH = ROOT / "mod-news.json"

LEGACY_SCRIPT_PATH = (
    ROOT / ".github" / "scripts" / "update_latest_release.py"
)

HMUC_GUID = "loupito75.HospitalModUpdateChecker"
CATALOG_FORMAT_VERSION = 1
CATALOG_VALIDITY_DAYS = 90
CATALOG_REFRESH_WHEN_DAYS_LEFT = 30
CATALOG_MAX_BYTES = 524288
CATALOG_MAX_MODS = 256
CATALOG_MAX_NEWS = 64


def load_legacy_tools():
    spec = importlib.util.spec_from_file_location(
        "hmuc_release_metadata_tools",
        str(LEGACY_SCRIPT_PATH),
    )

    if spec is None or spec.loader is None:
        raise RuntimeError(
            "Could not load existing update_latest_release.py."
        )

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TOOLS = load_legacy_tools()


def now_utc():
    return datetime.now(timezone.utc)


def utc_text(value):
    return (
        value.astimezone(timezone.utc)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z")
    )


def parse_utc(value, field_name):
    if not isinstance(value, str):
        raise RuntimeError(field_name + " must be a UTC string.")

    try:
        parsed = datetime.fromisoformat(
            value.replace("Z", "+00:00")
        )
    except ValueError as error:
        raise RuntimeError(
            "Invalid " + field_name + ": " + value
        ) from error

    if parsed.tzinfo is None:
        raise RuntimeError(field_name + " must include UTC timezone.")

    return parsed.astimezone(timezone.utc)


def load_json(path, required=True):
    if not path.exists():
        if required:
            raise RuntimeError(
                "Required JSON file is missing: " + str(path)
            )
        return None

    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise RuntimeError(
            "Invalid JSON file " + str(path) + ": " + str(error)
        ) from error


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def validate_news_item(item, label):
    if not isinstance(item, dict):
        raise RuntimeError(label + " must be an object.")

    allowed = {"id", "title", "text"}
    unknown = set(item) - allowed

    if unknown:
        raise RuntimeError(
            label
            + " contains unknown field(s): "
            + ", ".join(sorted(unknown))
        )

    news_id = item.get("id")
    title = item.get("title")
    text = item.get("text")

    if (
        type(news_id) is not int
        or news_id < 0
        or not isinstance(title, str)
        or not isinstance(text, str)
        or not title.strip()
        or not text.strip()
        or len(title) > 160
        or len(text) > 2048
        or "<" in title
        or ">" in title
        or "<" in text
        or ">" in text
    ):
        raise RuntimeError(label + " contains invalid news content.")

    for value in (title, text):
        for character in value:
            code = ord(character)
            if code < 32 and character not in "\n\r\t":
                raise RuntimeError(
                    label + " contains a forbidden control character."
                )

    return {
        "id": news_id,
        "title": title.strip(),
        "text": text.strip(),
    }


def load_control():
    value = load_json(CONTROL_PATH)

    if not isinstance(value, dict):
        raise RuntimeError(
            "catalog-control.json root must be an object."
        )

    allowed = {
        "formatVersion",
        "unifiedCatalogActive",
        "legacyDeleteAfterUtc",
        "legacyMigrationNews",
    }

    unknown = set(value) - allowed

    if unknown:
        raise RuntimeError(
            "catalog-control.json contains unknown field(s): "
            + ", ".join(sorted(unknown))
        )

    if value.get("formatVersion") != 1:
        raise RuntimeError(
            "catalog-control.json formatVersion must be 1."
        )

    active = value.get("unifiedCatalogActive")

    if type(active) is not bool:
        raise RuntimeError(
            "unifiedCatalogActive must be true or false."
        )

    delete_after = parse_utc(
        value.get("legacyDeleteAfterUtc"),
        "legacyDeleteAfterUtc",
    )

    migration_news = validate_news_item(
        value.get("legacyMigrationNews"),
        "legacyMigrationNews",
    )

    return {
        "active": active,
        "deleteAfter": delete_after,
        "migrationNews": migration_news,
    }


def load_news_source():
    value = load_json(NEWS_SOURCE_PATH)

    if not isinstance(value, dict):
        raise RuntimeError(
            "catalog/source/news.json root must be an object."
        )

    allowed = {"formatVersion", "items"}
    unknown = set(value) - allowed

    if unknown:
        raise RuntimeError(
            "catalog/source/news.json contains unknown field(s): "
            + ", ".join(sorted(unknown))
        )

    if value.get("formatVersion") != 1:
        raise RuntimeError(
            "catalog/source/news.json formatVersion must be 1."
        )

    items = value.get("items")

    if not isinstance(items, list):
        raise RuntimeError(
            "catalog/source/news.json items must be an array."
        )

    if len(items) > CATALOG_MAX_NEWS:
        raise RuntimeError(
            "catalog/source/news.json contains too many items."
        )

    result = []
    seen = set()

    for index, item in enumerate(items):
        parsed = validate_news_item(
            item,
            "news item " + str(index),
        )

        if parsed["id"] in seen:
            raise RuntimeError(
                "Duplicate news id: " + str(parsed["id"])
            )

        seen.add(parsed["id"])
        result.append(parsed)

    result.sort(key=lambda item: item["id"])
    return result


def empty_catalog():
    return {
        "formatVersion": CATALOG_FORMAT_VERSION,
        "manifestRevision": 0,
        "updatedUtc": "",
        "expiresUtc": "",
        "mods": [],
        "news": [],
    }


def load_catalog():
    if not CATALOG_PATH.exists():
        return empty_catalog()

    value = load_json(CATALOG_PATH)

    if not isinstance(value, dict):
        raise RuntimeError(
            "catalog/mod-catalog.json root must be an object."
        )

    if value.get("formatVersion") != CATALOG_FORMAT_VERSION:
        raise RuntimeError(
            "Unsupported catalog/mod-catalog.json formatVersion."
        )

    revision = value.get("manifestRevision")
    mods = value.get("mods")
    news = value.get("news")

    if (
        type(revision) is not int
        or revision < 1
        or not isinstance(mods, list)
        or not isinstance(news, list)
    ):
        raise RuntimeError(
            "catalog/mod-catalog.json has an invalid structure."
        )

    if len(mods) > CATALOG_MAX_MODS:
        raise RuntimeError(
            "catalog/mod-catalog.json contains too many mods."
        )

    if len(news) > CATALOG_MAX_NEWS:
        raise RuntimeError(
            "catalog/mod-catalog.json contains too many news items."
        )

    return value


def semantic_catalog(value):
    result = copy.deepcopy(value)
    result.pop("manifestRevision", None)
    result.pop("updatedUtc", None)
    result.pop("expiresUtc", None)
    return result


def package_to_nested(package, previous=None):
    nested = (
        copy.deepcopy(previous)
        if isinstance(previous, dict)
        else {}
    )

    for key, value in package.items():
        if key in {"guid", "name", "version"}:
            continue

        nested[key] = copy.deepcopy(value)

    return nested


def details_by_guid():
    value = load_json(DETAILS_PATH, required=False)

    if not isinstance(value, dict):
        return {}

    mods = value.get("mods")

    if not isinstance(mods, list):
        return {}

    result = {}

    for entry in mods:
        if not isinstance(entry, dict):
            continue

        guid = entry.get("guid")

        if isinstance(guid, str) and guid:
            result[guid.lower()] = entry

    return result


def updates_by_guid():
    value = load_json(UPDATE_MANIFEST_PATH, required=False)

    if not isinstance(value, dict):
        return {}

    mods = value.get("mods")

    if not isinstance(mods, list):
        return {}

    result = {}

    for entry in mods:
        if not isinstance(entry, dict):
            continue

        guid = entry.get("guid")

        if isinstance(guid, str) and guid:
            result[guid.lower()] = entry

    return result


def merge_legacy_into_catalog(catalog):
    package_manifest = load_json(
        PACKAGE_MANIFEST_PATH,
        required=False,
    )

    if not isinstance(package_manifest, dict):
        return

    packages = package_manifest.get("packages")

    if not isinstance(packages, list):
        return

    details = details_by_guid()
    updates = updates_by_guid()

    existing = {
        entry.get("guid", "").lower(): entry
        for entry in catalog.get("mods", [])
        if isinstance(entry, dict)
        and isinstance(entry.get("guid"), str)
    }

    for package in packages:
        if not isinstance(package, dict):
            raise RuntimeError(
                "mod-packages.json contains a non-object package."
            )

        guid = package.get("guid")
        name = package.get("name")
        version = package.get("version")

        if (
            not isinstance(guid, str)
            or not guid
            or not isinstance(name, str)
            or not name
            or not isinstance(version, str)
            or not version
        ):
            raise RuntimeError(
                "mod-packages.json contains incomplete package identity."
            )

        key = guid.lower()
        previous = existing.get(key)
        entry = (
            copy.deepcopy(previous)
            if isinstance(previous, dict)
            else {}
        )

        update = updates.get(key, {})
        detail = details.get(key, {})

        entry["guid"] = guid
        entry["name"] = name
        entry["author"] = (
            update.get("author")
            if isinstance(update.get("author"), str)
            else "Loupito75"
        )
        entry["version"] = version

        if isinstance(detail.get("description"), str):
            entry["description"] = detail.get("description", "")
        else:
            entry.setdefault("description", "")

        if isinstance(detail.get("whatsNew"), str):
            entry["whatsNew"] = detail.get("whatsNew", "")
        else:
            entry.setdefault("whatsNew", "")

        entry["package"] = package_to_nested(
            package,
            entry.get("package"),
        )

        existing[key] = entry

    catalog["mods"] = sorted(
        existing.values(),
        key=lambda entry: entry["guid"].lower(),
    )


def direct_release_entry(
    technical_mod_name,
    display_name,
    version,
    package,
):
    description, whats_new = TOOLS.get_public_mod_text(
        technical_mod_name,
        version,
    )

    return {
        "guid": package["guid"],
        "name": display_name,
        "author": "Loupito75",
        "version": version,
        "description": description,
        "whatsNew": whats_new,
        "package": package_to_nested(package),
    }


def update_catalog_entry(catalog, new_entry):
    mods = catalog.get("mods", [])
    target = new_entry["guid"].lower()
    found = None

    for entry in mods:
        if (
            isinstance(entry, dict)
            and isinstance(entry.get("guid"), str)
            and entry["guid"].lower() == target
        ):
            found = entry
            break

    if found is None:
        mods.append(new_entry)
    else:
        previous_version = found.get("version")

        if previous_version == new_entry["version"]:
            for key in (
                "guid",
                "name",
                "author",
                "version",
                "description",
                "whatsNew",
                "package",
            ):
                if found.get(key) != new_entry.get(key):
                    raise RuntimeError(
                        "Refusing to change already-published unified "
                        "catalog metadata for "
                        + new_entry["guid"]
                        + " "
                        + new_entry["version"]
                        + ". Publish a new version instead."
                    )
            return

        preserved = copy.deepcopy(found)
        preserved.update(new_entry)

        old_package = found.get("package")
        new_package = new_entry.get("package")

        if isinstance(old_package, dict) and isinstance(new_package, dict):
            merged_package = copy.deepcopy(old_package)
            merged_package.update(new_package)
            preserved["package"] = merged_package

        found.clear()
        found.update(preserved)

    catalog["mods"] = sorted(
        mods,
        key=lambda entry: entry["guid"].lower(),
    )


def process_release_direct(catalog):
    event = TOOLS.load_event()
    release, parsed = TOOLS.load_release(event)

    (
        technical_mod_name,
        version,
        release_name,
        release_url,
        published_at,
    ) = parsed

    print(
        "Processing release directly into unified catalog: "
        + technical_mod_name
        + " "
        + version
        + (
            " (" + release_name + ")"
            if release_name
            else ""
        )
    )

    if technical_mod_name == TOOLS.TEST_MOD_TECHNICAL_NAME:
        display_name = TOOLS.TEST_MOD_DISPLAY_NAME
    else:
        display_name = TOOLS.update_readme(
            technical_mod_name,
            version,
            release_url,
            published_at,
        )

    asset = TOOLS.find_release_asset(
        release,
        technical_mod_name,
        version,
    )

    with tempfile.TemporaryDirectory(
        prefix="hmuc-unified-release-"
    ) as temporary_directory:
        zip_path = Path(temporary_directory) / asset["name"]

        sha256, package_bytes = TOOLS.download_release_asset(
            asset,
            zip_path,
        )

        package = TOOLS.inspect_release_zip(
            zip_path=zip_path,
            technical_mod_name=technical_mod_name,
            display_name=display_name,
            version=version,
            download_url=asset["browser_download_url"],
            sha256=sha256,
            package_bytes=package_bytes,
        )

    update_catalog_entry(
        catalog,
        direct_release_entry(
            technical_mod_name,
            display_name,
            version,
            package,
        ),
    )


def rebuild_via_existing_release_tools(catalog):
    # The existing rebuild function already performs the expensive and
    # security-sensitive release ZIP inspection correctly. It may create full
    # legacy catalogs temporarily in the worktree; after merging them here,
    # apply_legacy_policy() either reduces or deletes them before publication.
    TOOLS.rebuild_package_catalog()
    merge_legacy_into_catalog(catalog)


def signature_envelope(raw_bytes):
    key = TOOLS.load_signing_key()
    signature = TOOLS.sign_manifest_bytes(
        raw_bytes,
        key,
    )

    return {
        "formatVersion": TOOLS.SIGNATURE_FORMAT_VERSION,
        "keyId": key["keyId"],
        "algorithm": TOOLS.SIGNATURE_ALGORITHM,
        "signature": base64.b64encode(signature).decode("ascii"),
    }


def catalog_needs_expiry_refresh(catalog):
    expires = catalog.get("expiresUtc")

    if not isinstance(expires, str) or not expires:
        return True

    try:
        expiry = parse_utc(expires, "catalog expiresUtc")
    except RuntimeError:
        return True

    return expiry <= now_utc() + timedelta(
        days=CATALOG_REFRESH_WHEN_DAYS_LEFT
    )


def seal_catalog(catalog, original_catalog, force_refresh=False):
    changed = (
        semantic_catalog(catalog)
        != semantic_catalog(original_catalog)
    )

    if (
        not changed
        and not force_refresh
        and not catalog_needs_expiry_refresh(catalog)
        and CATALOG_PATH.exists()
        and CATALOG_SIGNATURE_PATH.exists()
    ):
        print("Unified catalog is unchanged; no reseal required.")
        return False

    revision = catalog.get("manifestRevision", 0)

    if type(revision) is not int or revision < 0:
        raise RuntimeError(
            "Unified catalog manifestRevision must be an integer >= 0."
        )

    mods = catalog.get("mods")
    news = catalog.get("news")

    if (
        not isinstance(mods, list)
        or len(mods) > CATALOG_MAX_MODS
        or not isinstance(news, list)
        or len(news) > CATALOG_MAX_NEWS
    ):
        raise RuntimeError(
            "Unified catalog entry limits are invalid."
        )

    current = now_utc()

    catalog["formatVersion"] = CATALOG_FORMAT_VERSION
    catalog["manifestRevision"] = revision + 1
    catalog["updatedUtc"] = utc_text(current)
    catalog["expiresUtc"] = utc_text(
        current + timedelta(days=CATALOG_VALIDITY_DAYS)
    )
    catalog["mods"] = sorted(
        mods,
        key=lambda entry: entry["guid"].lower(),
    )
    catalog["news"] = sorted(
        news,
        key=lambda entry: entry["id"],
    )

    raw = (
        json.dumps(
            catalog,
            indent=2,
            ensure_ascii=False,
        )
        + "\n"
    ).encode("utf-8")

    if len(raw) > CATALOG_MAX_BYTES:
        raise RuntimeError(
            "Unified catalog exceeds "
            + str(CATALOG_MAX_BYTES)
            + " bytes."
        )

    CATALOG_PATH.parent.mkdir(
        parents=True,
        exist_ok=True,
    )
    CATALOG_PATH.write_bytes(raw)

    write_json(
        CATALOG_SIGNATURE_PATH,
        signature_envelope(raw),
    )

    print(
        "Unified catalog sealed. manifestRevision="
        + str(catalog["manifestRevision"])
        + " | expiresUtc="
        + catalog["expiresUtc"]
    )

    return True


def legacy_revision(path):
    value = load_json(path, required=False)

    if not isinstance(value, dict):
        return 0

    revision = value.get("manifestRevision")

    if type(revision) is int and revision >= 0:
        return revision

    return 0


def find_hmuc(catalog):
    for entry in catalog.get("mods", []):
        if (
            isinstance(entry, dict)
            and isinstance(entry.get("guid"), str)
            and entry["guid"].lower() == HMUC_GUID.lower()
        ):
            return entry

    raise RuntimeError(
        "Unified catalog does not contain Hospital Mod Update Checker."
    )


def legacy_same_entries(path, key, desired):
    value = load_json(path, required=False)

    return (
        isinstance(value, dict)
        and value.get("formatVersion") == 1
        and value.get(key) == desired
    )


def write_legacy_updates(hmuc):
    desired = [
        {
            "guid": hmuc["guid"],
            "name": hmuc["name"],
            "author": hmuc["author"],
            "version": hmuc["version"],
        }
    ]

    if legacy_same_entries(
        UPDATE_MANIFEST_PATH,
        "mods",
        desired,
    ):
        return

    write_json(
        UPDATE_MANIFEST_PATH,
        {
            "formatVersion": 1,
            "manifestRevision":
                legacy_revision(UPDATE_MANIFEST_PATH) + 1,
            "updatedUtc": utc_text(now_utc()),
            "mods": desired,
        },
    )


def write_legacy_signed(
    path,
    signature_path,
    collection_name,
    entries,
):
    current = load_json(path, required=False)

    current_entries = (
        current.get(collection_name)
        if isinstance(current, dict)
        else None
    )

    expiry_near = True

    if isinstance(current, dict):
        expires = current.get("expiresUtc")

        if isinstance(expires, str) and expires:
            try:
                expiry_near = (
                    parse_utc(
                        expires,
                        path.name + " expiresUtc",
                    )
                    <= now_utc()
                    + timedelta(
                        days=CATALOG_REFRESH_WHEN_DAYS_LEFT
                    )
                )
            except RuntimeError:
                expiry_near = True

    if (
        isinstance(current, dict)
        and current.get("formatVersion") == 1
        and current_entries == entries
        and not expiry_near
        and signature_path.exists()
    ):
        return

    current_time = now_utc()

    manifest = {
        "formatVersion": 1,
        "manifestRevision": legacy_revision(path) + 1,
        "updatedUtc": utc_text(current_time),
        "expiresUtc": utc_text(
            current_time
            + timedelta(days=TOOLS.PACKAGE_VALIDITY_DAYS)
        ),
        collection_name: entries,
    }

    raw = (
        json.dumps(
            manifest,
            indent=2,
            ensure_ascii=False,
        )
        + "\n"
    ).encode("utf-8")

    path.write_bytes(raw)
    write_json(
        signature_path,
        signature_envelope(raw),
    )


def write_legacy_packages(hmuc):
    package = copy.deepcopy(hmuc.get("package"))

    if not isinstance(package, dict):
        raise RuntimeError(
            "HMUC unified package metadata is missing."
        )

    package_entry = {
        "guid": hmuc["guid"],
        "name": hmuc["name"],
        "version": hmuc["version"],
    }
    package_entry.update(package)

    write_legacy_signed(
        PACKAGE_MANIFEST_PATH,
        PACKAGE_SIGNATURE_PATH,
        "packages",
        [package_entry],
    )


def write_legacy_details(hmuc):
    details_entry = {
        "guid": hmuc["guid"],
        "version": hmuc["version"],
        "description": hmuc.get("description", ""),
        "whatsNew": hmuc.get("whatsNew", ""),
    }

    write_legacy_signed(
        DETAILS_PATH,
        DETAILS_SIGNATURE_PATH,
        "mods",
        [details_entry],
    )


def write_legacy_news(migration_news):
    desired = [migration_news]

    if legacy_same_entries(
        NEWS_LEGACY_PATH,
        "items",
        desired,
    ):
        return

    write_json(
        NEWS_LEGACY_PATH,
        {
            "formatVersion": 1,
            "manifestRevision":
                legacy_revision(NEWS_LEGACY_PATH) + 1,
            "updatedUtc": utc_text(now_utc()),
            "items": desired,
        },
    )


def delete_legacy_update_catalogs():
    for path in (
        UPDATE_MANIFEST_PATH,
        PACKAGE_MANIFEST_PATH,
        PACKAGE_SIGNATURE_PATH,
        DETAILS_PATH,
        DETAILS_SIGNATURE_PATH,
    ):
        if path.exists():
            path.unlink()
            print("Deleted retired legacy file: " + str(path))


def apply_legacy_policy(catalog, control):
    if not control["active"]:
        print(
            "Unified catalog is staged only; legacy catalogs remain unchanged."
        )
        return

    migration_news = control["migrationNews"]
    write_legacy_news(migration_news)

    if now_utc() >= control["deleteAfter"]:
        delete_legacy_update_catalogs()
        print(
            "Legacy cutoff reached; mod-news.json is retained as the "
            "manual-migration notice."
        )
        return

    hmuc = find_hmuc(catalog)

    write_legacy_updates(hmuc)
    write_legacy_packages(hmuc)
    write_legacy_details(hmuc)

    print(
        "Legacy catalogs reduced to Hospital Mod Update Checker only."
    )


def workflow_mode():
    event_name = os.environ.get(
        "GITHUB_EVENT_NAME",
        "",
    ).strip()

    event = TOOLS.load_event()

    if event_name == "workflow_dispatch":
        inputs = event.get("inputs") or {}
        return (
            inputs.get("mode")
            or "single-release"
        ).strip()

    if event_name == "release":
        return "single-release"

    if event_name == "schedule":
        return "schedule"

    if event_name == "push":
        return "source-change"

    return "refresh-catalog"


def main():
    control = load_control()
    news = load_news_source()
    catalog = load_catalog()
    original_catalog = copy.deepcopy(catalog)
    mode = workflow_mode()
    event_name = os.environ.get(
        "GITHUB_EVENT_NAME",
        "",
    ).strip()

    if control["active"]:
        if mode == "single-release":
            process_release_direct(catalog)
        elif mode == "rebuild-package-catalog":
            rebuild_via_existing_release_tools(catalog)
        elif mode not in (
            "schedule",
            "source-change",
            "refresh-catalog",
        ):
            raise RuntimeError(
                "Unsupported unified-catalog mode: " + mode
            )
    else:
        # The established legacy publisher ran first when appropriate.
        # Mirror its working-tree outputs into the staged unified catalog.
        merge_legacy_into_catalog(catalog)

    catalog["news"] = news

    force_refresh = (
        mode == "refresh-catalog"
        or mode == "rebuild-package-catalog"
        or (
            event_name == "schedule"
            and os.environ.get(
                "GITHUB_EVENT_SCHEDULE",
                "",
            ).strip()
            == "17 4 1 * *"
        )
    )

    seal_catalog(
        catalog,
        original_catalog,
        force_refresh=force_refresh,
    )

    # Reload exact generated state if the seal changed revision/timestamps.
    catalog = load_catalog()
    apply_legacy_policy(catalog, control)


if __name__ == "__main__":
    main()
