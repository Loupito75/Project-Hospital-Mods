import json
import os
import re
from datetime import datetime, timezone
from pathlib import Path


README_PATH = Path("README.md")
MANIFEST_PATH = Path("mod-updates.json")

MANIFEST_AUTHOR = "Loupito75"
MANIFEST_GUID_PREFIX = "loupito75."


def normalize_name(value):
    return re.sub(r"[^a-z0-9]", "", value.lower())


def split_row(line):
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def build_row(cells):
    return "| " + " | ".join(cells) + " |"


def get_mod_name_from_cell(cell):
    match = re.search(r"\[([^\]]+)\]", cell)

    if match:
        return match.group(1)

    return cell.strip()


def humanize_technical_name(value):
    value = value.replace("_", " ").replace("-", " ")
    value = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", value)
    value = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", " ", value)
    return re.sub(r"\s+", " ", value).strip()


def get_release_display_name(release_name, technical_mod_name, version):
    if release_name:
        cleaned = re.sub(
            r"\s+v?" + re.escape(version) + r"\s*$",
            "",
            release_name,
            flags=re.IGNORECASE,
        ).strip()

        if cleaned:
            return cleaned

    return humanize_technical_name(technical_mod_name)


def load_release():
    event_path = os.environ.get("GITHUB_EVENT_PATH")

    if not event_path:
        raise RuntimeError("GITHUB_EVENT_PATH is not available.")

    with open(event_path, "r", encoding="utf-8") as file:
        event = json.load(file)

    release = event.get("release")

    if release:
        tag = (release.get("tag_name") or "").strip()
        release_name = (release.get("name") or "").strip()
        release_url = (release.get("html_url") or "").strip()
    else:
        inputs = event.get("inputs") or {}
        tag = (inputs.get("tag") or "").strip()

        if not tag:
            raise RuntimeError("No release tag was provided.")

        repository = os.environ.get("GITHUB_REPOSITORY", "").strip()

        if not repository:
            raise RuntimeError("GITHUB_REPOSITORY is not available.")

        release_name = ""
        release_url = (
            "https://github.com/"
            + repository
            + "/releases/tag/"
            + tag
        )

    if not tag or not release_url:
        raise RuntimeError("Release tag or URL is missing.")

    # Expected format:
    # HospitalTrafficControl-v1.2.0
    match = re.fullmatch(r"(.+)-v(\d+\.\d+\.\d+)", tag)

    if not match:
        raise RuntimeError(
            "Release tag must use the format ModName-vX.Y.Z: " + tag
        )

    technical_mod_name = match.group(1)
    version = match.group(2)

    return technical_mod_name, version, release_name, release_url


def update_readme(
    technical_mod_name,
    version,
    release_name,
    release_url,
):
    latest_release = f"[{version}]({release_url})"
    target_name = normalize_name(technical_mod_name)

    text = README_PATH.read_text(encoding="utf-8")
    lines = text.splitlines()

    mods_heading = None

    for index, line in enumerate(lines):
        if line.strip().lower() == "## mods":
            mods_heading = index
            break

    if mods_heading is None:
        raise RuntimeError("Could not find the '## Mods' section in README.md.")

    section_end = len(lines)

    for index in range(mods_heading + 1, len(lines)):
        if lines[index].startswith("## "):
            section_end = index
            break

    header_index = None

    for index in range(mods_heading + 1, section_end):
        if not lines[index].strip().startswith("|"):
            continue

        cells = split_row(lines[index])

        if "Mod" in cells and "Latest release" in cells:
            header_index = index
            break

    if header_index is None:
        raise RuntimeError("Could not find the Mods table.")

    headers = split_row(lines[header_index])
    mod_column = headers.index("Mod")
    latest_column = headers.index("Latest release")

    found = False
    display_name = ""
    last_table_row = header_index + 1

    for index in range(header_index + 2, section_end):
        line = lines[index]

        if not line.strip().startswith("|"):
            break

        last_table_row = index
        cells = split_row(line)

        while len(cells) < len(headers):
            cells.append("")

        mod_name = get_mod_name_from_cell(cells[mod_column])

        if normalize_name(mod_name) != target_name:
            continue

        display_name = mod_name
        cells[latest_column] = latest_release
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
        display_name = get_release_display_name(
            release_name,
            technical_mod_name,
            version,
        )

        cells = [""] * len(headers)
        cells[mod_column] = display_name
        cells[latest_column] = latest_release

        lines.insert(last_table_row + 1, build_row(cells))

        print(
            "Mod was not found in README table. "
            "Added new row for: "
            + display_name
        )

    new_text = "\n".join(lines) + "\n"

    if new_text == text:
        print("README.md is already up to date.")
    else:
        README_PATH.write_text(new_text, encoding="utf-8")
        print("README.md updated.")

    return display_name


def update_manifest(technical_mod_name, display_name, version):
    if not MANIFEST_PATH.exists():
        raise RuntimeError("mod-updates.json was not found.")

    with MANIFEST_PATH.open("r", encoding="utf-8") as file:
        manifest = json.load(file)

    if manifest.get("formatVersion") != 1:
        raise RuntimeError(
            "Unsupported manifest formatVersion: "
            + str(manifest.get("formatVersion"))
        )

    revision = manifest.get("manifestRevision")

    if (
        not isinstance(revision, int)
        or isinstance(revision, bool)
        or revision < 1
    ):
        raise RuntimeError("manifestRevision must be an integer >= 1.")

    mods = manifest.get("mods")

    if not isinstance(mods, list):
        raise RuntimeError("Manifest 'mods' must be an array.")

    guid = MANIFEST_GUID_PREFIX + technical_mod_name
    target_guid = guid.lower()

    matches = []

    for candidate in mods:
        if not isinstance(candidate, dict):
            raise RuntimeError("Manifest contains a non-object mod entry.")

        candidate_guid = candidate.get("guid")

        if (
            isinstance(candidate_guid, str)
            and candidate_guid.lower() == target_guid
        ):
            matches.append(candidate)

    if len(matches) > 1:
        raise RuntimeError(
            "Manifest contains duplicate entries for GUID: " + guid
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
            "Added manifest entry for "
            + display_name
            + " at version "
            + version
            + "."
        )
    else:
        entry = matches[0]
        current_version = entry.get("version")

        if current_version != version:
            entry["version"] = version
            manifest_changed = True

            print(
                "Updated manifest version for "
                + display_name
                + " from "
                + str(current_version)
                + " to "
                + version
                + "."
            )
        else:
            print(
                "Manifest version for "
                + display_name
                + " is already "
                + version
                + "."
            )

    if not manifest_changed:
        print("mod-updates.json is already up to date.")
        return

    manifest["manifestRevision"] = revision + 1
    manifest["updatedUtc"] = (
        datetime.now(timezone.utc)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z")
    )

    MANIFEST_PATH.write_text(
        json.dumps(
            manifest,
            indent=2,
            ensure_ascii=False,
        )
        + "\n",
        encoding="utf-8",
    )

    print(
        "mod-updates.json updated. manifestRevision="
        + str(manifest["manifestRevision"])
        + " | updatedUtc="
        + manifest["updatedUtc"]
    )


def main():
    (
        technical_mod_name,
        version,
        release_name,
        release_url,
    ) = load_release()

    display_name = update_readme(
        technical_mod_name,
        version,
        release_name,
        release_url,
    )

    update_manifest(
        technical_mod_name,
        display_name,
        version,
    )


if __name__ == "__main__":
    main()
