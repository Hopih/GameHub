import { renderGameCards } from "../components/game-card.js";

export function createCatalog({ favorites, onViewChange, onRetry }) {
    const state = { games: [], loaded: false, genre: "all", query: "", savedOnly: false, sort: "popular" };
    const collator = new Intl.Collator("ru");
    const ui = {
        grid: document.getElementById("games"),
        search: document.getElementById("game-search"),
        title: document.getElementById("catalog-title"),
        count: document.getElementById("catalog-count"),
        status: document.getElementById("catalog-status"),
        loadedCount: document.getElementById("loaded-count"),
        empty: document.getElementById("empty-state"),
        emptyTitle: document.getElementById("empty-title"),
        emptyDescription: document.getElementById("empty-description"),
        error: document.getElementById("load-error"),
        retry: document.getElementById("retry-load"),
        favoriteFilter: document.getElementById("favorite-filter"),
        genres: document.querySelector(".genre-filters")
    };

    function render() {
        if (!state.loaded) return;

        const games = state.games.filter(game =>
            (state.genre === "all" || (game.genres || []).some(genre => genre.slug === state.genre)) &&
            (!state.savedOnly || favorites.has(game.id)) &&
            game.name.toLocaleLowerCase("ru").includes(state.query)
        );
        if (state.sort === "rating") games.sort((a, b) => b.rating - a.rating);
        if (state.sort === "name") games.sort((a, b) => collator.compare(a.name, b.name));
        if (state.sort === "newest") games.sort((a, b) => (Date.parse(b.released) || 0) - (Date.parse(a.released) || 0));

        renderGameCards(ui.grid, games, favorites);
        ui.count.textContent = games.length;
        ui.title.textContent = state.savedOnly ? "Отмеченные игры" : "Каталог игр";
        ui.status.textContent = state.query
            ? `Результаты по запросу «${ui.search.value.trim()}» среди загруженных игр` : "";
        ui.loadedCount.textContent = `Показано ${games.length} из ${state.games.length} загруженных игр`;
        ui.empty.hidden = games.length > 0;
        ui.emptyTitle.textContent = state.savedOnly ? "Пока нет подходящих отметок" : "Ничего не нашлось";
        ui.emptyDescription.textContent = state.savedOnly
            ? "Нажми на сердечко на карточке игры или сбрось фильтры. Отметки действуют до перезагрузки страницы."
            : "Попробуй другое название или выбери другой жанр.";
        updateFilters();
    }

    function updateFilters() {
        ui.genres.querySelectorAll("[data-genre]").forEach(button => {
            const active = button.dataset.genre === state.genre;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-pressed", String(active));
        });
        ui.favoriteFilter.classList.toggle("is-active", state.savedOnly);
        ui.favoriteFilter.setAttribute("aria-pressed", String(state.savedOnly));
    }

    function show(savedOnly = false) {
        state.genre = "all";
        state.query = "";
        state.savedOnly = savedOnly;
        ui.search.value = "";
        updateFilters();
        render();
    }

    ui.search.addEventListener("input", () => {
        state.query = ui.search.value.trim().toLocaleLowerCase("ru");
        render();
    });
    document.getElementById("search-form").addEventListener("submit", event => {
        event.preventDefault();
        document.getElementById("catalog").scrollIntoView();
        onViewChange(state.savedOnly ? "library" : "catalog");
    });
    document.getElementById("game-sort").addEventListener("change", event => {
        state.sort = event.target.value;
        render();
    });
    ui.genres.addEventListener("click", event => {
        const button = event.target.closest("[data-genre]");
        if (!button) return;
        state.genre = button.dataset.genre;
        updateFilters();
        render();
    });
    ui.favoriteFilter.addEventListener("click", () => {
        state.savedOnly = !state.savedOnly;
        onViewChange(state.savedOnly ? "library" : "catalog");
        updateFilters();
        render();
    });
    document.getElementById("reset-filters").addEventListener("click", () => {
        show();
        onViewChange("catalog");
    });
    ui.retry.addEventListener("click", onRetry);
    favorites.subscribe(() => { if (state.savedOnly) render(); });

    return {
        show,
        showGenre(genre) {
            show();
            state.genre = genre;
            updateFilters();
            render();
        },
        startLoading() {
            state.loaded = false;
            ui.retry.disabled = true;
            ui.error.hidden = true;
            ui.empty.hidden = true;
            ui.status.textContent = "Загружаем игры из каталога…";
            ui.grid.setAttribute("aria-busy", "true");
        },
        setGames(games) {
            state.games = games;
            state.loaded = true;
            ui.retry.disabled = false;
            render();
        },
        showError() {
            state.loaded = false;
            ui.grid.replaceChildren();
            ui.grid.setAttribute("aria-busy", "false");
            ui.status.textContent = "";
            ui.count.textContent = "0";
            ui.loadedCount.textContent = "Игры пока не загружены";
            ui.empty.hidden = true;
            ui.error.hidden = false;
            ui.retry.disabled = false;
        }
    };
}
