import { getGames, getFeaturedGames } from "./api/games.js";
import { createSession } from "./api/session.js";
import { bindAccount } from "./features/account.js";
import { createCatalog } from "./features/catalog.js";
import { createDiscovery } from "./features/discovery.js";
import { createFavorites, bindFavoriteControls } from "./features/favorites.js";
import { initNavigation, setActiveNav } from "./features/navigation.js";
import { createNotifier } from "./ui/notification.js";

const notify = createNotifier();
const session = createSession({
    onSignInRequired: message => openLogin(message),
    onError: error => notify(error.message)
});
const openLogin = bindAccount(session, notify);
const favorites = createFavorites(session);
bindFavoriteControls(favorites, session, openLogin, notify);

const catalog = createCatalog({ favorites, onViewChange: setActiveNav, onPageChange: loadCatalog });
const discovery = createDiscovery(favorites);
initNavigation(catalog);
let discoveryLoaded = false;

async function loadCatalog(page = 1, sort = "popular") {
    if (!catalog.startLoading(page, sort)) return;
    if (!discoveryLoaded) discovery.startLoading();
    try {
        const data = await getGames(page, sort);
        catalog.setPage(data, sort);
        if (!discoveryLoaded) {
            discovery.setGames(data.results);
            discoveryLoaded = true;
        }
    } catch (error) {
        console.error("Не удалось загрузить каталог:", error);
        catalog.showError();
        if (!discoveryLoaded) discovery.showError();
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

document.getElementById("retry-spotlights").addEventListener("click", loadSpotlights);
loadCatalog();
loadSpotlights();
void session.restore().catch(error => notify(error.message));
