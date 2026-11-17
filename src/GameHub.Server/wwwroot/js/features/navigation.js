export function setActiveNav(name) {
    document.querySelectorAll(".main-nav [data-nav]").forEach(link => {
        const active = link.dataset.nav === name;
        link.classList.toggle("is-active", active);
        if (active) link.setAttribute("aria-current", "location");
        else link.removeAttribute("aria-current");
    });
}

export function initNavigation(catalog) {
    document.addEventListener("click", event => {
        const navigation = event.target.closest("[data-nav]");
        if (navigation) {
            const name = navigation.dataset.nav;
            setActiveNav(name);
            if (name === "catalog" || name === "library") {
                catalog.show(name === "library");
            }
        }

        const genreLink = event.target.closest("[data-set-genre]");
        if (genreLink) {
            catalog.showGenre(genreLink.dataset.setGenre);
            document.getElementById("catalog").scrollIntoView();
            setActiveNav("catalog");
        }
    });

    document.addEventListener("keydown", event => {
        if (event.key === "/" && !event.ctrlKey && !event.metaKey && !event.altKey
            && !document.activeElement.matches("input, textarea, select, [contenteditable]")) {
            event.preventDefault();
            document.getElementById("game-search").focus();
        }
    });
}
