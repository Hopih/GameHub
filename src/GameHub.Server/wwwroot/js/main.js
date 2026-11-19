 import { getGames, getFeaturedGames } from "./api/games.js";
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
    document.getElementById("retry-recommendations").addEventListener("click", loadRecommendations);
    void loadRecommendations();

    async function loadRecommendations() {
        recommendations.startLoading();
        try {
            recommendations.setGames((await getGames()).results);
        } catch (error) {
            console.error("Не удалось загрузить рекомендации:", error);
            recommendations.showError();
        }
    }
}
