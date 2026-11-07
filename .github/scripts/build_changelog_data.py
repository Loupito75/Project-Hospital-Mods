#!/usr/bin/env python3

import datetime
import html
import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path


REPOSITORY = os.environ.get(
    "GITHUB_REPOSITORY",
    "Loupito75/Project-Hospital-Mods"
)

TOKEN = os.environ.get("GITHUB_TOKEN", "").strip()

ROOT = Path(__file__).resolve().parents[2]
CATALOG_PATH = ROOT / "catalog" / "mod-catalog.json"
README_PATH = ROOT / "README.md"
ROADMAP_PATH = ROOT / "ROADMAP.md"
REDIRECTS_PATH = ROOT / "docs" / "republished-versions.json"
OUTPUT_PATH = ROOT / "docs" / "changelog-data.json"
README_OUTPUT_PATH = ROOT / "docs" / "readme.html"
ROADMAP_OUTPUT_PATH = ROOT / "docs" / "roadmap-data.json"

TAG_RE = re.compile(
    r"^(?P<mod>[A-Za-z0-9][A-Za-z0-9._-]*)"
    r"-v(?P<version>[0-9]+(?:\.[0-9]+){2,3})"
    r"(?P<republished>-republished)?$"
)

EXCLUDED_MOD_IDS = {
    "HospitalModUpdateTest",
}

PUBLIC_REPO_URL = (
    "https://github.com/"
    + REPOSITORY
)
PUBLIC_RAW_URL = (
    "https://raw.githubusercontent.com/"
    + REPOSITORY
    + "/main/"
)


def api_request(url):
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "Project-Hospital-Mods-Changelog-Builder",
        "X-GitHub-Api-Version": "2022-11-28",
    }

    if TOKEN:
        headers["Authorization"] = "Bearer " + TOKEN

    request = urllib.request.Request(
        url,
        headers=headers,
        method="GET",
    )

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(
            "GitHub API request failed: "
            + str(exc.code)
            + " "
            + url
            + "\n"
            + detail
        ) from exc


def load_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def fetch_all_releases():
    releases = []
    page = 1

    while True:
        url = (
            "https://api.github.com/repos/"
            + REPOSITORY
            + "/releases?per_page=100&page="
            + str(page)
        )

        batch = api_request(url)

        if not isinstance(batch, list):
            raise RuntimeError("Unexpected GitHub releases response.")

        releases.extend(batch)

        if len(batch) < 100:
            break

        page += 1

    return releases


def render_markdown(markdown):
    if not markdown:
        return ""

    payload = {
        "text": markdown,
        "mode": "gfm",
        "context": REPOSITORY,
    }

    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "Project-Hospital-Mods-Changelog-Builder",
        "X-GitHub-Api-Version": "2022-11-28",
        "Content-Type": "application/json",
    }

    if TOKEN:
        headers["Authorization"] = "Bearer " + TOKEN

    request = urllib.request.Request(
        "https://api.github.com/markdown",
        data=json.dumps(payload).encode("utf-8"),
        headers=headers,
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(
            "GitHub Markdown rendering failed: "
            + str(exc.code)
            + "\n"
            + detail
        ) from exc


def catalog_names():
    catalog = load_json(CATALOG_PATH)
    result = {}

    for mod in catalog.get("mods", []):
        guid = str(mod.get("guid", ""))
        name = str(mod.get("name", "")).strip()

        prefix = "loupito75."

        if not guid.lower().startswith(prefix):
            continue

        technical = guid[len(prefix):]

        if technical and name:
            result[technical] = name

    return result


def fallback_mod_name(release, technical, version):
    name = str(release.get("name") or "").strip()

    if name:
        suffixes = [
            " " + version,
            " v" + version,
        ]

        for suffix in suffixes:
            if name.endswith(suffix):
                candidate = name[:-len(suffix)].strip()

                if candidate:
                    return candidate

    return technical


def version_key(version):
    parts = []

    for piece in version.split("."):
        try:
            parts.append(int(piece))
        except ValueError:
            parts.append(0)

    return tuple(parts)


def first_download_url(release):
    assets = release.get("assets") or []

    for asset in assets:
        name = str(asset.get("name") or "").lower()

        if name.endswith(".zip"):
            return asset.get("browser_download_url")

    if assets:
        return assets[0].get("browser_download_url")

    return None


def normalized_release(release, names):
    tag = str(release.get("tag_name") or "")
    match = TAG_RE.match(tag)

    if not match:
        return None

    technical = match.group("mod")

    if technical in EXCLUDED_MOD_IDS:
        return None

    version = match.group("version")
    published = (
        release.get("published_at")
        or release.get("created_at")
        or ""
    )

    return {
        "modId": technical,
        "modName": names.get(
            technical,
            fallback_mod_name(release, technical, version)
        ),
        "version": version,
        "sourceVersion": version,
        "sourceTag": tag,
        "republished": bool(match.group("republished")),
        "historicalAlias": False,
        "prerelease": bool(release.get("prerelease")),
        "publishedUtc": published,
        "releaseUrl": release.get("html_url"),
        "downloadUrl": first_download_url(release),
        "bodyHtml": render_markdown(str(release.get("body") or "")),
    }


def load_redirects():
    if not REDIRECTS_PATH.exists():
        return []

    data = load_json(REDIRECTS_PATH)

    if data.get("formatVersion") != 1:
        raise RuntimeError(
            "Unsupported republished-versions.json formatVersion."
        )

    redirects = data.get("redirects", [])

    if not isinstance(redirects, list):
        raise RuntimeError(
            "republished-versions.json redirects must be an array."
        )

    return redirects


def apply_redirects(mod_map, release_by_tag, names):
    seen = set()

    for redirect in load_redirects():
        technical = str(redirect.get("mod") or "").strip()
        target_tag = str(redirect.get("targetTag") or "").strip()
        versions = redirect.get("versions")

        if not technical or not target_tag:
            raise RuntimeError(
                "Each republished redirect needs mod and targetTag."
            )

        if not isinstance(versions, list) or not versions:
            raise RuntimeError(
                "Republished redirect for "
                + technical
                + " needs at least one version."
            )

        source = release_by_tag.get(target_tag)

        if source is None:
            raise RuntimeError(
                "Republished target release not found: " + target_tag
            )

        if source["modId"] != technical:
            raise RuntimeError(
                "Republished target "
                + target_tag
                + " belongs to "
                + source["modId"]
                + ", not "
                + technical
            )

        version_map = mod_map.setdefault(technical, {})

        for value in versions:
            version = str(value).strip()

            if not re.match(r"^[0-9]+(?:\.[0-9]+){2,3}$", version):
                raise RuntimeError(
                    "Invalid historical version "
                    + repr(version)
                    + " for "
                    + technical
                )

            duplicate_key = (technical, version)

            if duplicate_key in seen:
                raise RuntimeError(
                    "Duplicate historical redirect for "
                    + technical
                    + " "
                    + version
                )

            seen.add(duplicate_key)

            item = dict(source)
            item["version"] = version
            item["historicalAlias"] = (
                version != source["sourceVersion"]
            )
            item["modName"] = names.get(
                technical,
                source["modName"]
            )

            version_map[version] = item


def github_heading_slug(value):
    text = re.sub(r"<[^>]+>", "", value)
    text = html.unescape(text).strip().lower()
    text = re.sub(r"[^\w\- ]+", "", text, flags=re.UNICODE)
    text = re.sub(r"\s+", "-", text)
    text = re.sub(r"-+", "-", text)
    return text.strip("-")


def add_heading_ids(rendered):
    seen = {}

    def replace_heading(match):
        level = match.group(1)
        attributes = match.group(2) or ""
        body = match.group(3)

        if re.search(r"\bid\s*=", attributes, flags=re.IGNORECASE):
            return match.group(0)

        slug = github_heading_slug(body)

        if not slug:
            return match.group(0)

        count = seen.get(slug, 0)
        seen[slug] = count + 1

        if count:
            slug = slug + "-" + str(count)

        return (
            "<h"
            + level
            + attributes
            + ' id="'
            + slug
            + '">'
            + body
            + "</h"
            + level
            + ">"
        )

    return re.sub(
        r"<h([1-6])(\s[^>]*)?>(.*?)</h\1>",
        replace_heading,
        rendered,
        flags=re.IGNORECASE | re.DOTALL,
    )


def normalize_github_anchor_ids(rendered):
    rendered = re.sub(
        r'\b(id|name)=(["\'])user-content-([^"\']+)\2',
        lambda match: (
            match.group(1)
            + "="
            + match.group(2)
            + match.group(3)
            + match.group(2)
        ),
        rendered,
        flags=re.IGNORECASE,
    )

    rendered = re.sub(
        r'href=(["\'])#user-content-([^"\']+)\1',
        lambda match: (
            "href="
            + match.group(1)
            + "#"
            + match.group(2)
            + match.group(1)
        ),
        rendered,
        flags=re.IGNORECASE,
    )

    return rendered


def rewrite_readme_links(rendered):
    rendered = normalize_github_anchor_ids(rendered)

    def replace_named_anchor(match):
        attributes = match.group(1)
        name_match = re.search(
            r'\bname=["\']([^"\']+)["\']',
            attributes,
            flags=re.IGNORECASE,
        )

        if not name_match:
            return match.group(0)

        if re.search(r"\bid\s*=", attributes, flags=re.IGNORECASE):
            return match.group(0)

        value = name_match.group(1)

        return (
            "<a"
            + attributes
            + ' id="'
            + value
            + '">'
        )

    rendered = re.sub(
        r"<a(\s[^>]*)>",
        replace_named_anchor,
        rendered,
        flags=re.IGNORECASE,
    )

    def replace_href(match):
        quote = match.group(1)
        value = match.group(2).strip()

        if (
            not value
            or value.startswith("#")
            or value.startswith("/")
            or re.match(
                r"^[a-z][a-z0-9+.-]*:",
                value,
                flags=re.IGNORECASE,
            )
        ):
            return match.group(0)

        path = value.lstrip("./")

        if path.casefold() == "roadmap.md":
            return "href=" + quote + "roadmap.html" + quote

        if path.endswith("/"):
            target = (
                PUBLIC_REPO_URL
                + "/tree/main/"
                + path.rstrip("/")
            )
        else:
            target = (
                PUBLIC_REPO_URL
                + "/blob/main/"
                + path
            )

        return "href=" + quote + target + quote

    rendered = re.sub(
        r'href=(["\'])([^"\']+)\1',
        replace_href,
        rendered,
        flags=re.IGNORECASE,
    )

    def replace_src(match):
        quote = match.group(1)
        value = match.group(2).strip()

        if (
            not value
            or value.startswith("/")
            or re.match(
                r"^[a-z][a-z0-9+.-]*:",
                value,
                flags=re.IGNORECASE,
            )
        ):
            return match.group(0)

        target = PUBLIC_RAW_URL + value.lstrip("./")
        return "src=" + quote + target + quote

    rendered = re.sub(
        r'src=(["\'])([^"\']+)\1',
        replace_src,
        rendered,
        flags=re.IGNORECASE,
    )

    rendered = add_heading_ids(rendered)
    return normalize_github_anchor_ids(rendered)


def roadmap_slug(value):
    text = str(value or "").strip().lower()
    text = re.sub(r"[^a-z0-9]+", "-", text)
    return text.strip("-")


def parse_markdown_table_row(line):
    value = line.strip()

    if not value.startswith("|") or not value.endswith("|"):
        return None

    cells = [
        cell.strip()
        for cell in value[1:-1].split("|")
    ]

    return cells


def build_roadmap_data(names):
    if not ROADMAP_PATH.exists():
        raise RuntimeError(
            "Missing ROADMAP.md."
        )

    text = ROADMAP_PATH.read_text(encoding="utf-8")
    lines = text.splitlines()

    updated_match = re.search(
        r"\*\*Last updated:\*\*\s*([0-9]{4}-[0-9]{2}-[0-9]{2})",
        text,
    )
    last_updated = (
        updated_match.group(1)
        if updated_match
        else None
    )

    name_to_technical = {
        name.casefold(): technical
        for technical, name in names.items()
    }

    mods = []
    current_mod = None
    index = 0

    while index < len(lines):
        line = lines[index].strip()

        heading_match = re.match(
            r"^##\s+(.+?)\s+\(([A-Za-z0-9]+)\)\s*$",
            line,
        )

        if heading_match:
            mod_name = heading_match.group(1).strip()
            acronym = heading_match.group(2).strip()
            technical = name_to_technical.get(
                mod_name.casefold()
            )

            current_mod = {
                "id": technical or roadmap_slug(mod_name),
                "technicalId": technical,
                "name": mod_name,
                "acronym": acronym,
                "items": [],
            }
            mods.append(current_mod)
            index += 1
            continue

        if (
            current_mod is not None
            and line
            == "| Status | Type | Feature | Description |"
        ):
            index += 2

            while index < len(lines):
                row_line = lines[index].strip()

                if not row_line.startswith("|"):
                    break

                cells = parse_markdown_table_row(row_line)

                if cells is None or len(cells) != 4:
                    break

                status, item_type, feature, description = cells

                status_text = re.sub(
                    r"^[^A-Za-z]+",
                    "",
                    status,
                ).strip()

                current_mod["items"].append({
                    "status": status,
                    "statusId": roadmap_slug(status_text),
                    "statusLabel": status_text,
                    "type": item_type,
                    "typeId": roadmap_slug(item_type),
                    "feature": feature,
                    "description": description,
                })

                index += 1

            continue

        index += 1

    mods = [
        mod
        for mod in mods
        if mod["items"]
    ]

    statuses = {}
    types = {}
    total_items = 0

    for mod in mods:
        for item in mod["items"]:
            total_items += 1

            statuses.setdefault(
                item["statusId"],
                {
                    "id": item["statusId"],
                    "label": item["statusLabel"],
                    "display": item["status"],
                    "count": 0,
                },
            )
            statuses[item["statusId"]]["count"] += 1

            types.setdefault(
                item["typeId"],
                {
                    "id": item["typeId"],
                    "label": item["type"],
                    "count": 0,
                },
            )
            types[item["typeId"]]["count"] += 1

    payload = {
        "formatVersion": 1,
        "lastUpdated": last_updated,
        "totalItems": total_items,
        "statuses": list(statuses.values()),
        "types": sorted(
            types.values(),
            key=lambda item: item["label"].casefold(),
        ),
        "mods": mods,
    }

    ROADMAP_OUTPUT_PATH.parent.mkdir(
        parents=True,
        exist_ok=True,
    )
    ROADMAP_OUTPUT_PATH.write_text(
        json.dumps(
            payload,
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


def build_readme_html():
    if not README_PATH.exists():
        raise RuntimeError(
            "Missing README.md."
        )

    markdown = README_PATH.read_text(encoding="utf-8")
    rendered = render_markdown(markdown)
    rendered = rewrite_readme_links(rendered)

    README_OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    README_OUTPUT_PATH.write_text(
        rendered + "\n",
        encoding="utf-8",
    )


def main():
    if not CATALOG_PATH.exists():
        raise RuntimeError(
            "Missing catalog/mod-catalog.json."
        )

    build_readme_html()

    names = catalog_names()
    build_roadmap_data(names)
    raw_releases = fetch_all_releases()

    release_by_tag = {}
    canonical = {}

    for release in raw_releases:
        if release.get("draft"):
            continue

        item = normalized_release(release, names)

        if item is None:
            continue

        release_by_tag[item["sourceTag"]] = item

        key = (item["modId"], item["version"])
        current = canonical.get(key)

        if (
            current is None
            or item["publishedUtc"] > current["publishedUtc"]
        ):
            canonical[key] = item

    mod_map = {}

    for item in canonical.values():
        versions = mod_map.setdefault(item["modId"], {})
        versions[item["version"]] = item

    apply_redirects(mod_map, release_by_tag, names)

    mods = []

    for technical, version_map in mod_map.items():
        items = list(version_map.values())
        items.sort(
            key=lambda item: version_key(item["version"]),
            reverse=True,
        )

        display_name = names.get(
            technical,
            items[0]["modName"] if items else technical,
        )

        mods.append({
            "id": technical,
            "name": display_name,
            "versions": items,
        })

    mods.sort(key=lambda mod: mod["name"].casefold())

    recent = list(canonical.values())
    recent.sort(
        key=lambda item: item["publishedUtc"],
        reverse=True,
    )
    recent = recent[:20]

    now = datetime.datetime.now(datetime.timezone.utc)
    generated = now.isoformat(timespec="seconds").replace("+00:00", "Z")

    payload = {
        "formatVersion": 1,
        "generatedUtc": generated,
        "repository": REPOSITORY,
        "mods": mods,
        "recent": recent,
    }

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    print("Generated " + str(README_OUTPUT_PATH) + ".")
    print("Generated " + str(ROADMAP_OUTPUT_PATH) + ".")

    print(
        "Generated "
        + str(OUTPUT_PATH)
        + " with "
        + str(len(mods))
        + " mods and "
        + str(len(canonical))
        + " version entries."
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print("ERROR: " + str(exc), file=sys.stderr)
        sys.exit(1)
