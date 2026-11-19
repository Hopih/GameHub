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

    const query = new URLSearchParams(location.search).get("q");
    if (query) catalog.setQuery(query);
    document.getElementById("retry-spotlights").addEventListener("click", loadSpotlights);
    void loadCatalog();
    void loadSpotlights();

    async function loadCatalog(page = 1, sort = "popular") {
        if (!catalog.startLoading(page, sort)) return;
        try {
            const data = await getGames(page, sort);
            catalog.setPage(data, sort);
            if (!previewLoaded) {
                discovery.setGames(data.results);
                previewLoaded = true;
            }
        } catch (error) {
            console.error("Не удалось загрузить каталог:", error);
            catalog.showError();
            if (!previewLoaded) discovery.showError();
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
