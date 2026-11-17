import { updateFavoriteButton } from "../components/game-card.js";

export function createFavorites() {
    // Пока живёт в памяти страницы. Позже здесь появятся запросы к нашей БД через API.
    const ids = new Set();
    const listeners = new Set();

    return {
        has(id) { return ids.has(id); },
        get count() { return ids.size; },
        toggle(id) {
            if (ids.has(id)) ids.delete(id);
            else ids.add(id);
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

    document.addEventListener("click", event => {
        const button = event.target.closest("[data-favorite-id]");
        if (!button) return;

        const id = Number(button.dataset.favoriteId);
        favorites.toggle(id);
        notify(favorites.has(id) ? "Игра отмечена на этой странице" : "Отметка снята");
    });
}
