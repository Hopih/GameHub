import { getGames, getFeaturedGames } from "./api/games.js";
import { createCatalog } from "./features/catalog.js";
import { createDiscovery } from "./features/discovery.js";
import { createFavorites, bindFavoriteControls } from "./features/favorites.js";
import { initNavigation, setActiveNav } from "./features/navigation.js";
import { createNotifier } from "./ui/notification.js";

const favorites = createFavorites();
bindFavoriteControls(favorites, createNotifier());

const catalog = createCatalog({ favorites, onViewChange: setActiveNav, onRetry: loadCatalog });
const discovery = createDiscovery(favorites);
initNavigation(catalog);

async function loadCatalog() {
    catalog.startLoading();
    discovery.startLoading();
    try {
        const games = await getGames();
        catalog.setGames(games);
        discovery.setGames(games);
    } catch (error) {
        console.error("Не удалось загрузить каталог:", error);
        catalog.showError();
        discovery.showError();
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
