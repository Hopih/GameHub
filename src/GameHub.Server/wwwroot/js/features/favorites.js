import { updateFavoriteButton } from "../components/game-card.js";

export function createFavorites() {
    // Пока живёт в памяти страницы. Позже здесь появятся запросы к нашей БД через API.
    const games = new Map();
    const listeners = new Set();

    return {
        has(id) { return games.has(id); },
        get count() { return games.size; },
        getGames() { return [...games.values()]; },
        toggle(game) {
            if (games.has(game.id)) games.delete(game.id);
            else games.set(game.id, game);
            listeners.forEach(listener => listener());
        },
        subscribe(listener) {
            listeners.add(listener);
            return () => listeners.delete(listener);
        }
    };
}

export function bindFavoriteControls(favorites, notify) {
    const badge = document.getElementById("saved-count");
    favorites.subscribe(() => {
        document.querySelectorAll("[data-favorite-id]").forEach(button => {
            updateFavoriteButton(button, favorites.has(Number(button.dataset.favoriteId)));
        });
        badge.textContent = favorites.count;
        badge.hidden = favorites.count === 0;
    });

    document.addEventListener("favorite-toggle", event => {
        const game = event.detail;
        favorites.toggle(game);
        notify(favorites.has(game.id) ? "Игра отмечена" : "Отметка снята");
    });
}
