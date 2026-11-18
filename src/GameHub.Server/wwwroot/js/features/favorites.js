import { updateFavoriteButton } from "../components/game-card.js";

export function createFavorites(session) {
    const games = new Map();
    const listeners = new Set();
    const updating = new Set();
    let status = "guest";
    let accountVersion = 0;
    let loading = null;

    function publish() {
        listeners.forEach(listener => listener());
    }

    function load() {
        if (!session.profile) return;
        if (loading) return loading;
        const version = accountVersion;
        status = "loading";
        publish();

        loading = (async () => {
            try {
                const response = await session.request("/favorites");
                const saved = await response.json();
                if (version !== accountVersion) return;
                games.clear();
                saved.forEach(game => games.set(game.id, game));
                status = "ready";
            } catch {
                if (version === accountVersion) status = "error";
            } finally {
                if (version === accountVersion) {
                    loading = null;
                    publish();
                }
            }
        })();
        return loading;
    }

    session.subscribe(profile => {
        accountVersion++;
        games.clear();
        updating.clear();
        loading = null;
        if (profile) void load();
        else {
            status = "guest";
            publish();
        }
    });

    return {
        has(id) { return games.has(id); },
        get count() { return games.size; },
        get status() { return status; },
        getGames() { return [...games.values()]; },
        load,
        async toggle(game) {
            const version = accountVersion;
            if (status !== "ready") await load();
            if (version !== accountVersion || updating.has(game.id)) return;
            if (status !== "ready") throw new Error("Не удалось загрузить избранное.");

            const remove = games.has(game.id);
            updating.add(game.id);
            try {
                if (remove) {
                    await session.request(`/favorites?id=${game.id}`, { method: "DELETE" });
                } else {
                    await session.request("/favorites", {
                        method: "PUT",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({
                            id: game.id, name: game.name, slug: game.slug,
                            background_image: game.background_image, metacritic: game.metacritic,
                            added: game.added, released: game.released,
                            genres: game.genres?.map(({ slug, name }) => ({ slug, name }))
                        })
                    });
                }
                if (version !== accountVersion) return;
                if (remove) games.delete(game.id);
                else games.set(game.id, game);
                publish();
                return !remove;
            } finally {
                if (version === accountVersion) updating.delete(game.id);
            }
        },
        subscribe(listener) {
            listeners.add(listener);
            return () => listeners.delete(listener);
        }
    };
}

export function bindFavoriteControls(favorites, session, openLogin, notify) {
    const badge = document.getElementById("saved-count");
    favorites.subscribe(() => {
        document.querySelectorAll("[data-favorite-id]").forEach(button => {
            updateFavoriteButton(button, favorites.has(Number(button.dataset.favoriteId)));
        });
        badge.textContent = favorites.count;
        badge.hidden = favorites.count === 0;
    });

    document.addEventListener("favorite-toggle", async event => {
        if (!session.profile) {
            openLogin("Войди, чтобы сохранить игру в избранном.");
            return;
        }
        try {
            const selected = await favorites.toggle(event.detail);
            if (selected !== undefined) notify(selected ? "Игра отмечена" : "Отметка снята");
        } catch (error) {
            if (session.profile) notify(error.message);
        }
    });
}
