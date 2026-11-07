#!/usr/bin/env python3

import datetime
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
REDIRECTS_PATH = ROOT / "docs" / "republished-versions.json"
OUTPUT_PATH = ROOT / "docs" / "changelog-data.json"
README_OUTPUT_PATH = ROOT / "docs" / "readme.html"

TAG_RE = re.compile(
    r"^(?P<mod>[A-Za-z0-9][A-Za-z0-9._-]*)"
    r"-v(?P<version>[0-9]+(?:\.[0-9]+){2,3})"
    r"(?P<republished>-republished)?$"
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


def build_readme_html():
    if not README_PATH.exists():
        raise RuntimeError(
            "Missing README.md."
        )

    markdown = README_PATH.read_text(encoding="utf-8")
    rendered = render_markdown(markdown)

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
