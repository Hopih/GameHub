export function initNavigation(catalog) {
    const search = document.getElementById("game-search");
    document.addEventListener("keydown", event => {
        if (event.key === "/" && !event.ctrlKey && !event.metaKey && !event.altKey
            && !document.activeElement.matches("input, textarea, select, [contenteditable]")) {
            event.preventDefault();
            search.focus();
        }
    });

    if (document.getElementById("main-content").dataset.currentPage !== "home") return;

    const links = document.querySelectorAll(".main-nav [data-nav]");
    function updateActiveLink() {
        links.forEach(link => {
            const active = link.hash !== "" && link.hash === location.hash;
            link.classList.toggle("is-active", active);
            if (active) link.setAttribute("aria-current", "location");
            else link.removeAttribute("aria-current");
        });
    }

    document.addEventListener("click", event => {
        const genreLink = event.target.closest("[data-set-genre]");
        if (!genreLink) return;
        catalog.showGenre(genreLink.dataset.setGenre);
        scrollToCatalog();
    });

    window.addEventListener("hashchange", updateActiveLink);
    updateActiveLink();
}

export function scrollToCatalog() {
    if (location.hash !== "#catalog") location.hash = "catalog";
    else document.getElementById("catalog").scrollIntoView();
}
