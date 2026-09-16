import json
import os
import re
from pathlib import Path


README_PATH = Path("README.md")


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


def main():
    event_path = os.environ.get("GITHUB_EVENT_PATH")

    if not event_path:
        raise RuntimeError("GITHUB_EVENT_PATH is not available.")

    with open(event_path, "r", encoding="utf-8") as file:
        event = json.load(file)

    release = event.get("release")

    if not release:
        raise RuntimeError("No release information found in GitHub event.")

    tag = release.get("tag_name", "").strip()
    release_name = (release.get("name") or "").strip()
    release_url = release.get("html_url", "").strip()

    if not tag or not release_url:
        raise RuntimeError("Release tag or URL is missing.")

    # Expected format:
    # HospitalTrafficControl-v1.2.0
    match = re.match(r"^(.+)-v(\d+\.\d+\.\d+)$", tag)

    if match:
        technical_mod_name = match.group(1)
        version = match.group(2)
        latest_release = f"[{version}]({release_url})"
    else:
        technical_mod_name = tag.split("-v", 1)[0]
        latest_release = f"[{tag}]({release_url})"

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
        if lines[index].strip().startswith("|"):
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

        if normalize_name(mod_name) == target_name:
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
        display_name = release_name if release_name else tag

        cells = [""] * len(headers)

        cells[mod_column] = display_name
        cells[latest_column] = latest_release

        new_row = build_row(cells)

        lines.insert(last_table_row + 1, new_row)

        print(
            "Mod was not found in README table. "
            "Added new row for: "
            + display_name
        )

    new_text = "\n".join(lines) + "\n"

    if new_text == text:
        print("README is already up to date.")
        return

    README_PATH.write_text(new_text, encoding="utf-8")

    print("README.md updated.")


if __name__ == "__main__":
    main()
