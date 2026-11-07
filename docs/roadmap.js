"use strict";

const ROADMAP_DATA_URL = "roadmap-data.json";

let roadmapData = null;

document.addEventListener("DOMContentLoaded", initializeRoadmap);

async function initializeRoadmap() {
    const controls = [
        document.getElementById("roadmap-mod"),
        document.getElementById("roadmap-status"),
        document.getElementById("roadmap-type"),
        document.getElementById("roadmap-search")
    ];

    for (const control of controls) {
        control.addEventListener(
            control.tagName === "INPUT" ? "input" : "change",
            onFiltersChanged
        );
    }

    document
        .getElementById("clear-filters")
        .addEventListener("click", clearFilters);

    try {
        const response = await fetch(
            ROADMAP_DATA_URL,
            {
                cache: "no-cache"
            });

        if (!response.ok) {
            throw new Error(
                "HTTP " + response.status);
        }

        roadmapData = await response.json();

        populateFilters();
        applyUrlFilters();
        renderSummary();
        renderRoadmap();

        for (const control of controls) {
            control.disabled = false;
        }
    }
    catch (error) {
        console.error(error);

        setRoadmapStatus(
            "Unable to load the roadmap. Please try again later.",
            true
        );
    }
}

function populateFilters() {
    const modSelect =
        document.getElementById("roadmap-mod");
    const statusSelect =
        document.getElementById("roadmap-status");
    const typeSelect =
        document.getElementById("roadmap-type");

    modSelect.innerHTML = "";
    modSelect.add(
        new Option("All mods", ""));

    for (const mod of roadmapData.mods) {
        modSelect.add(
            new Option(
                mod.name + " (" + mod.acronym + ")",
                mod.id
            ));
    }

    statusSelect.innerHTML = "";
    statusSelect.add(
        new Option("All statuses", ""));

    for (const status of roadmapData.statuses) {
        statusSelect.add(
            new Option(
                status.display + " (" + status.count + ")",
                status.id
            ));
    }

    typeSelect.innerHTML = "";
    typeSelect.add(
        new Option("All types", ""));

    for (const type of roadmapData.types) {
        typeSelect.add(
            new Option(
                type.label + " (" + type.count + ")",
                type.id
            ));
    }
}

function applyUrlFilters() {
    const params =
        new URLSearchParams(window.location.search);

    setSelectIfValid(
        "roadmap-mod",
        params.get("mod"));

    setSelectIfValid(
        "roadmap-status",
        params.get("status"));

    setSelectIfValid(
        "roadmap-type",
        params.get("type"));

    const search = params.get("q");

    if (search) {
        document.getElementById(
            "roadmap-search").value = search;
    }
}

function setSelectIfValid(id, value) {
    if (!value) {
        return;
    }

    const select = document.getElementById(id);

    if (
        Array.from(select.options)
            .some((option) => option.value === value)
    ) {
        select.value = value;
    }
}

function onFiltersChanged() {
    renderRoadmap();
    updateRoadmapUrl();
}

function clearFilters() {
    document.getElementById("roadmap-mod").value = "";
    document.getElementById("roadmap-status").value = "";
    document.getElementById("roadmap-type").value = "";
    document.getElementById("roadmap-search").value = "";

    renderRoadmap();
    updateRoadmapUrl();
}

function renderSummary() {
    const container =
        document.getElementById("roadmap-summary");

    container.innerHTML = "";

    for (const status of roadmapData.statuses) {
        const card =
            document.createElement("button");

        card.type = "button";
        card.className =
            "roadmap-summary-card status-" +
            status.id;

        const value =
            document.createElement("strong");

        value.textContent =
            String(status.count);

        const label =
            document.createElement("span");

        label.textContent =
            status.display;

        card.appendChild(value);
        card.appendChild(label);

        card.addEventListener(
            "click",
            () => {
                document.getElementById(
                    "roadmap-status").value =
                    status.id;

                renderRoadmap();
                updateRoadmapUrl();

                document.getElementById(
                    "roadmap-status").focus();
            });

        container.appendChild(card);
    }
}

function renderRoadmap() {
    const content =
        document.getElementById("roadmap-content");

    const modFilter =
        document.getElementById("roadmap-mod").value;
    const statusFilter =
        document.getElementById("roadmap-status").value;
    const typeFilter =
        document.getElementById("roadmap-type").value;
    const search =
        document.getElementById("roadmap-search")
            .value
            .trim()
            .toLocaleLowerCase();

    content.innerHTML = "";

    let visibleItems = 0;
    let visibleMods = 0;

    for (const mod of roadmapData.mods) {
        if (
            modFilter &&
            mod.id !== modFilter
        ) {
            continue;
        }

        const items = mod.items.filter((item) => {
            if (
                statusFilter &&
                item.statusId !== statusFilter
            ) {
                return false;
            }

            if (
                typeFilter &&
                item.typeId !== typeFilter
            ) {
                return false;
            }

            if (search) {
                const haystack = (
                    item.feature +
                    " " +
                    item.description +
                    " " +
                    item.type +
                    " " +
                    mod.name +
                    " " +
                    mod.acronym
                ).toLocaleLowerCase();

                if (!haystack.includes(search)) {
                    return false;
                }
            }

            return true;
        });

        if (items.length === 0) {
            continue;
        }

        visibleMods += 1;
        visibleItems += items.length;

        content.appendChild(
            createModSection(mod, items));
    }

    if (visibleItems === 0) {
        const empty =
            document.createElement("section");

        empty.className =
            "card roadmap-empty";

        empty.innerHTML =
            "<h2>No matching roadmap items</h2>" +
            "<p>Try changing or clearing one of the filters.</p>";

        content.appendChild(empty);
    }

    let message =
        visibleItems +
        (visibleItems === 1 ? " item" : " items") +
        " across " +
        visibleMods +
        (visibleMods === 1 ? " mod" : " mods");

    if (roadmapData.lastUpdated) {
        message +=
            " · Last updated " +
            formatRoadmapDate(
                roadmapData.lastUpdated);
    }

    setRoadmapStatus(message + ".");
}

function createModSection(mod, items) {
    const section =
        document.createElement("section");

    section.className = "roadmap-mod-card";

    const header =
        document.createElement("div");

    header.className = "roadmap-mod-heading";

    const titleBox =
        document.createElement("div");

    const kicker =
        document.createElement("p");

    kicker.className = "section-eyebrow";
    kicker.textContent = mod.acronym;

    const title =
        document.createElement("h2");

    title.textContent = mod.name;

    titleBox.appendChild(kicker);
    titleBox.appendChild(title);

    header.appendChild(titleBox);

    if (mod.technicalId) {
        const changelog =
            document.createElement("a");

        changelog.href =
            "changelog.html?mod=" +
            encodeURIComponent(mod.technicalId);

        changelog.textContent =
            "View changelogs";

        header.appendChild(changelog);
    }

    section.appendChild(header);

    const list =
        document.createElement("div");

    list.className = "roadmap-item-list";

    for (const item of items) {
        list.appendChild(
            createRoadmapItem(item));
    }

    section.appendChild(list);

    return section;
}

function createRoadmapItem(item) {
    const article =
        document.createElement("article");

    article.className =
        "roadmap-item status-" +
        item.statusId;

    const top =
        document.createElement("div");

    top.className = "roadmap-item-top";

    const labels =
        document.createElement("div");

    labels.className = "roadmap-labels";

    const status =
        document.createElement("span");

    status.className =
        "roadmap-status-badge status-" +
        item.statusId;

    status.textContent = item.status;

    const type =
        document.createElement("span");

    type.className = "roadmap-type-badge";
    type.textContent = item.type;

    labels.appendChild(status);
    labels.appendChild(type);

    top.appendChild(labels);

    const title =
        document.createElement("h3");

    title.textContent = item.feature;

    const description =
        document.createElement("p");

    description.textContent =
        item.description;

    article.appendChild(top);
    article.appendChild(title);
    article.appendChild(description);

    return article;
}

function updateRoadmapUrl() {
    const url =
        new URL(window.location.href);

    updateParam(
        url,
        "mod",
        document.getElementById(
            "roadmap-mod").value);

    updateParam(
        url,
        "status",
        document.getElementById(
            "roadmap-status").value);

    updateParam(
        url,
        "type",
        document.getElementById(
            "roadmap-type").value);

    updateParam(
        url,
        "q",
        document.getElementById(
            "roadmap-search").value.trim());

    window.history.replaceState(
        null,
        "",
        url);
}

function updateParam(url, name, value) {
    if (value) {
        url.searchParams.set(name, value);
    }
    else {
        url.searchParams.delete(name);
    }
}

function setRoadmapStatus(message, isError) {
    const status =
        document.getElementById(
            "roadmap-status-text");

    status.textContent = message;
    status.classList.toggle(
        "error",
        Boolean(isError));
}

function formatRoadmapDate(value) {
    const parts = value.split("-");

    if (parts.length !== 3) {
        return value;
    }

    const date = new Date(
        Number(parts[0]),
        Number(parts[1]) - 1,
        Number(parts[2]));

    return new Intl.DateTimeFormat(
        undefined,
        {
            year: "numeric",
            month: "long",
            day: "numeric"
        }).format(date);
}
