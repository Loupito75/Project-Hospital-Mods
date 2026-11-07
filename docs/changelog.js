"use strict";

const DATA_URL = "changelog-data.json";
const ALL_MODS_VALUE = "__all__";

let changelogData = null;

document.addEventListener("DOMContentLoaded", initialize);

async function initialize() {
    const modSelect = document.getElementById("mod-select");
    const versionSelect = document.getElementById("version-select");

    modSelect.addEventListener("change", onModChanged);
    versionSelect.addEventListener("change", onVersionChanged);

    try {
        const response = await fetch(DATA_URL, {
            cache: "no-cache"
        });

        if (!response.ok) {
            throw new Error("HTTP " + response.status);
        }

        changelogData = await response.json();

        populateModSelect();
        applyUrlSelection();
        renderSelection();

        setStatus(
            "Generated from published GitHub Releases on " +
            formatDateTime(changelogData.generatedUtc) +
            "."
        );
    }
    catch (error) {
        console.error(error);
        setStatus(
            "Unable to load changelog data. Please try again later.",
            true
        );
    }
}

function populateModSelect() {
    const select = document.getElementById("mod-select");

    select.innerHTML = "";

    select.add(new Option("All mods", ALL_MODS_VALUE));

    for (const mod of changelogData.mods) {
        select.add(new Option(mod.name, mod.id));
    }

    select.disabled = false;
}

function applyUrlSelection() {
    const params = new URLSearchParams(window.location.search);
    const requestedMod = params.get("mod");
    const requestedVersion = params.get("version");

    const modSelect = document.getElementById("mod-select");

    if (
        requestedMod &&
        changelogData.mods.some((mod) => mod.id === requestedMod)
    ) {
        modSelect.value = requestedMod;
    }
    else {
        modSelect.value = ALL_MODS_VALUE;
    }

    populateVersionSelect(requestedVersion);
}

function onModChanged() {
    populateVersionSelect(null);
    renderSelection();
    updateUrl();
}

function onVersionChanged() {
    renderSelection();
    updateUrl();
}

function populateVersionSelect(preferredVersion) {
    const modSelect = document.getElementById("mod-select");
    const versionSelect = document.getElementById("version-select");

    versionSelect.innerHTML = "";

    if (modSelect.value === ALL_MODS_VALUE) {
        versionSelect.add(new Option("Recent releases", ""));
        versionSelect.disabled = true;
        return;
    }

    const mod = getSelectedMod();

    if (!mod || mod.versions.length === 0) {
        versionSelect.add(new Option("No releases", ""));
        versionSelect.disabled = true;
        return;
    }

    for (const item of mod.versions) {
        let label = item.version;

        if (item.prerelease) {
            label += " (pre-release)";
        }

        versionSelect.add(new Option(label, item.version));
    }

    versionSelect.disabled = false;

    if (
        preferredVersion &&
        mod.versions.some((item) => item.version === preferredVersion)
    ) {
        versionSelect.value = preferredVersion;
    }
    else {
        versionSelect.value = mod.versions[0].version;
    }
}

function renderSelection() {
    const modSelect = document.getElementById("mod-select");
    const content = document.getElementById("content");

    content.innerHTML = "";

    if (modSelect.value === ALL_MODS_VALUE) {
        renderRecentReleases(content);
        return;
    }

    const mod = getSelectedMod();
    const versionSelect = document.getElementById("version-select");

    if (!mod) {
        return;
    }

    const item = mod.versions.find(
        (candidate) => candidate.version === versionSelect.value
    );

    if (!item) {
        return;
    }

    content.appendChild(createReleaseCard(item, mod.name, true));
}

function renderRecentReleases(container) {
    const heading = document.createElement("p");
    heading.className = "recent-title";
    heading.textContent =
        "Latest published releases across all Project Hospital mods.";
    container.appendChild(heading);

    for (const item of changelogData.recent) {
        container.appendChild(
            createReleaseCard(item, item.modName, false)
        );
    }
}

function createReleaseCard(item, modName, showHistoricalNotice) {
    const article = document.createElement("article");
    article.className = "release-card";

    const heading = document.createElement("div");
    heading.className = "release-heading";

    const titleBox = document.createElement("div");

    const title = document.createElement("h2");
    title.textContent = modName + " " + item.version;

    if (item.prerelease) {
        const badge = document.createElement("span");
        badge.className = "badge";
        badge.textContent = "Pre-release";
        title.appendChild(badge);
    }

    const meta = document.createElement("p");
    meta.className = "release-meta";
    meta.textContent = "Published " + formatDate(item.publishedUtc);

    titleBox.appendChild(title);
    titleBox.appendChild(meta);

    const actions = document.createElement("div");
    actions.className = "release-actions";

    actions.appendChild(
        createLinkButton(
            "View GitHub Release",
            item.releaseUrl,
            true
        )
    );

    if (item.downloadUrl) {
        actions.appendChild(
            createLinkButton(
                "Download",
                item.downloadUrl,
                false
            )
        );
    }

    heading.appendChild(titleBox);
    heading.appendChild(actions);

    article.appendChild(heading);

    if (showHistoricalNotice && item.historicalAlias) {
        const notice = document.createElement("div");
        notice.className = "notice";
        notice.textContent =
            "Version " +
            item.version +
            " is an archived historical version. " +
            "Its changelog is included in the consolidated " +
            item.sourceVersion +
            " republished release.";
        article.appendChild(notice);
    }

    const body = document.createElement("div");
    body.className = "markdown-body";
    body.innerHTML = item.bodyHtml || "<p>No changelog was provided.</p>";

    article.appendChild(body);

    return article;
}

function createLinkButton(text, href, secondary) {
    const link = document.createElement("a");
    link.className = secondary ? "button secondary" : "button";
    link.href = href;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    link.textContent = text;
    return link;
}

function getSelectedMod() {
    const value = document.getElementById("mod-select").value;

    return changelogData.mods.find((mod) => mod.id === value) || null;
}

function updateUrl() {
    const modSelect = document.getElementById("mod-select");
    const versionSelect = document.getElementById("version-select");
    const url = new URL(window.location.href);

    if (modSelect.value === ALL_MODS_VALUE) {
        url.searchParams.delete("mod");
        url.searchParams.delete("version");
    }
    else {
        url.searchParams.set("mod", modSelect.value);

        if (versionSelect.value) {
            url.searchParams.set("version", versionSelect.value);
        }
        else {
            url.searchParams.delete("version");
        }
    }

    window.history.replaceState(null, "", url);
}

function setStatus(message, isError) {
    const status = document.getElementById("status");

    status.textContent = message;
    status.classList.toggle("error", Boolean(isError));
}

function formatDate(value) {
    if (!value) {
        return "unknown date";
    }

    const date = new Date(value);

    return new Intl.DateTimeFormat(undefined, {
        year: "numeric",
        month: "long",
        day: "numeric"
    }).format(date);
}

function formatDateTime(value) {
    if (!value) {
        return "an unknown date";
    }

    const date = new Date(value);

    return new Intl.DateTimeFormat(undefined, {
        year: "numeric",
        month: "long",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit"
    }).format(date);
}
