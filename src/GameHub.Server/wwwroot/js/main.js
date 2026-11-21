import { getGames, getFeaturedGames, getRecommendations } from "./api/games.js";
import { createSession } from "./api/session.js";
import { bindAccount } from "./features/account.js";
import { createCatalog } from "./features/catalog.js";
import { createDiscovery, createRecommendations } from "./features/discovery.js";
import { createFavorites, bindFavoriteControls } from "./features/favorites.js";
import { initNavigation, scrollToCatalog } from "./features/navigation.js";
import { createNotifier } from "./ui/notification.js";

const notify = createNotifier();
const session = createSession({
    onSignInRequired: message => openLogin(message),
    onError: error => notify(error.message)
});
const openLogin = bindAccount(session, notify);
const favorites = createFavorites(session);
bindFavoriteControls(favorites, session, openLogin, notify);

const page = document.getElementById("main-content").dataset.currentPage;
if (page === "recommendations") {
    initNavigation();
    initRecommendations();
} else {
    initCatalog(page === "library");
}
void session.restore().catch(error => notify(error.message));

function initCatalog(isLibrary) {
    const discovery = isLibrary ? null : createDiscovery(favorites);
    let previewLoaded = false;
    let latestRequest = 0;
    const catalog = createCatalog({
        favorites,
        onViewChange(view) {
            if (isLibrary && view === "catalog") location.assign("/#catalog");
            else scrollToCatalog();
        },
        onPageChange: loadCatalog
    });
    initNavigation(catalog);

    if (isLibrary) {
        catalog.show(true);
        return;
    }

    const query = new URLSearchParams(location.search).get("q")?.trim();
    if (query) {
        catalog.setQuery(query);
        void getGames().then(setPreview).catch(() => {
            if (!previewLoaded) discovery.showError();
        });
    } else {
        void loadCatalog();
    }
    document.getElementById("retry-spotlights").addEventListener("click", loadSpotlights);
    void loadSpotlights();

    function setPreview(data) {
        if (previewLoaded) return;
        discovery.setGames(data.results);
        previewLoaded = true;
    }

    async function loadCatalog(page = 1, sort = "popular", query = "") {
        if (!catalog.startLoading(page, sort, query)) return;
        const request = ++latestRequest;
        const includesPreview = !query && page === 1 && sort === "popular";
        try {
            const data = await getGames(page, sort, query);
            if (request === latestRequest) catalog.setPage(data, sort);
            if (includesPreview) setPreview(data);
        } catch (error) {
            console.error("Не удалось загрузить каталог:", error);
            if (request === latestRequest) catalog.showError();
            if (includesPreview && !previewLoaded) discovery.showError();
        }
    }

    async function loadSpotlights() {
        discovery.startSpotlightLoading();
        try {
            discovery.setSpotlights(await getFeaturedGames());
        } catch (error) {
            console.error("Не удалось загрузить подборку:", error);
            discovery.showSpotlightError();
        }
    }
}

function initRecommendations() {
    const recommendations = createRecommendations(favorites);
    const form = document.getElementById("recommendation-form");
    const description = document.getElementById("recommendation-description");
    const reset = document.getElementById("reset-recommendations");
    let activeDescription = "";
    let latestRequest = 0;

    form.addEventListener("submit", event => {
        event.preventDefault();
        description.value = description.value.trim();
        if (!description.reportValidity()) return;
        activeDescription = description.value;
        void loadRecommendations();
    });
    reset.addEventListener("click", () => {
        form.reset();
        activeDescription = "";
        description.focus();
        void loadRecommendations();
    });
    document.getElementById("retry-recommendations").addEventListener("click", loadRecommendations);
    void loadRecommendations();

    async function loadRecommendations() {
        const query = activeDescription;
        const request = ++latestRequest;
        reset.hidden = !query;
        recommendations.startLoading(Boolean(query));
        try {
            const data = query ? await getRecommendations(query) : await getGames();
            if (request !== latestRequest) return;
            if (query) recommendations.setMatches(data);
            else recommendations.setGames(data.results);
        } catch (error) {
            if (request !== latestRequest) return;
            console.error("Не удалось загрузить рекомендации:", error);
            recommendations.showError(Boolean(query));
        }
    }
}
