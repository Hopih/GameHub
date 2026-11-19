import { renderGameCards } from "../components/game-card.js";
import { createPagination } from "../components/pagination.js";

export function createCatalog({ favorites, onViewChange, onPageChange }) {
    const state = {
        games: [], loaded: false, loading: false, loadFailed: false,
        page: 1, requestedPage: 1, hasNext: false,
        genre: "all", query: "", libraryQuery: "", savedOnly: false,
        sort: "popular", requestedSort: "popular", librarySort: "popular"
    };
    let searchTimer;
    const pagination = document.getElementById("catalog-pagination")
        ? createPagination({
            onPrevious: () => onPageChange(state.page - 1, state.sort, state.query),
            onNext: () => onPageChange(state.page + 1, state.sort, state.query)
        })
        : null;
    const collator = new Intl.Collator("ru");
    const ui = {
        grid: document.getElementById("games"),
        search: document.getElementById("game-search"),
        sort: document.getElementById("game-sort"),
        title: document.getElementById("catalog-title"),
        count: document.getElementById("catalog-count"),
        status: document.getElementById("catalog-status"),
        loadedCount: document.getElementById("loaded-count"),
        empty: document.getElementById("empty-state"),
        emptyTitle: document.getElementById("empty-title"),
        emptyDescription: document.getElementById("empty-description"),
        reset: document.getElementById("reset-filters"),
        signIn: document.getElementById("library-sign-in"),
        error: document.getElementById("load-error"),
        retry: document.getElementById("retry-load"),
        retryFavorites: document.getElementById("retry-favorites"),
        genres: document.querySelector(".genre-filters")
    };

    const loadingCards = [...ui.grid.children];

    function render() {
        const catalogLoading = !state.savedOnly && state.loading;
        const catalogError = !state.savedOnly && state.loadFailed;
        const source = state.savedOnly ? favorites.getGames() : state.games;
        const libraryStatus = state.savedOnly ? favorites.status : null;
        const games = source.filter(game =>
            (state.genre === "all" || (game.genres || []).some(genre => genre.slug === state.genre)) &&
            (!state.savedOnly || game.name.toLocaleLowerCase("ru").includes(state.libraryQuery))
        );
        // Каталог уже отсортирован RAWG. Локально сортируем только личные отметки.
        if (state.savedOnly) {
            if (state.librarySort === "popular") games.sort((a, b) => (b.added || 0) - (a.added || 0));
            if (state.librarySort === "rating") games.sort((a, b) => (b.metacritic ?? -1) - (a.metacritic ?? -1));
            if (state.librarySort === "name") games.sort((a, b) => collator.compare(a.name, b.name));
            if (state.librarySort === "newest") games.sort((a, b) => (Date.parse(b.released) || 0) - (Date.parse(a.released) || 0));
        }

        if (catalogLoading && !state.loaded) ui.grid.replaceChildren(...loadingCards);
        else renderGameCards(ui.grid, games, favorites);
        ui.grid.setAttribute("aria-busy", String(libraryStatus === "loading" || catalogLoading));
        ui.count.textContent = games.length;
        ui.title.textContent = state.savedOnly ? "Отмеченные игры" : "Каталог игр";
        const query = state.savedOnly ? ui.search.value.trim() : state.query;
        ui.status.textContent = catalogLoading
            ? query ? `Ищем игры по запросу «${query}»…` : `Загружаем страницу ${state.requestedPage}…`
            : query ? `Результаты по запросу «${query}»${state.savedOnly ? " среди отмеченных игр" : ""}` : "";
        if (catalogError) ui.status.textContent = `Не удалось загрузить страницу ${state.requestedPage}.`;
        if (libraryStatus === "loading") ui.status.textContent = "Загружаем избранное…";
        if (libraryStatus === "error") ui.status.textContent = "Не удалось загрузить избранное.";
        if (ui.retryFavorites) ui.retryFavorites.hidden = libraryStatus !== "error";
        if (ui.error) ui.error.hidden = !catalogError;
        if (ui.retry) ui.retry.disabled = !catalogError;
        ui.loadedCount.textContent = state.savedOnly
            ? `Показано ${games.length} из ${source.length} отмеченных игр`
            : `Показано ${games.length} из ${source.length} игр страницы`;
        if (!state.savedOnly && !state.loaded) ui.loadedCount.textContent = "Игры пока не загружены";
        ui.empty.hidden = games.length > 0 || libraryStatus === "loading" || libraryStatus === "error" ||
            catalogLoading || catalogError;
        ui.emptyTitle.textContent = libraryStatus === "guest" ? "Войди, чтобы увидеть избранное"
            : state.savedOnly ? "Пока нет подходящих отметок" : "Ничего не нашлось";
        ui.emptyDescription.textContent = state.savedOnly
            ? libraryStatus === "guest" ? "Сохранённые игры привязаны к аккаунту."
                : "Нажми на сердечко на карточке игры или сбрось фильтры."
            : "Попробуй другое название или выбери другой жанр.";
        ui.reset.textContent = libraryStatus === "guest" ? "Открыть каталог" : "Сбросить фильтры";
        if (ui.signIn) ui.signIn.hidden = libraryStatus !== "guest";
        updateFilters();
    }

    function updateFilters() {
        ui.sort.disabled = !state.savedOnly && state.loading;
        ui.sort.value = state.savedOnly ? state.librarySort : state.loading ? state.requestedSort : state.sort;
        ui.genres.querySelectorAll("[data-genre]").forEach(button => {
            const active = button.dataset.genre === state.genre;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-pressed", String(active));
        });
        pagination?.render({
            page: state.page,
            hasNext: state.hasNext,
            disabled: state.loading || !state.loaded,
            hidden: state.savedOnly
        });
    }

    function show(savedOnly = false, genre = "all") {
        clearTimeout(searchTimer);
        state.genre = genre;
        state.libraryQuery = "";
        state.savedOnly = savedOnly;
        ui.search.value = "";
        if (!savedOnly && state.query) search(true);
        else render();
    }

    function search(immediate = false) {
        clearTimeout(searchTimer);
        const query = ui.search.value.trim();
        if (state.savedOnly) {
            state.libraryQuery = query.toLocaleLowerCase("ru");
            render();
        } else if (query !== state.query) {
            const load = () => onPageChange(1, state.requestedSort, query);
            if (immediate || !query) load();
            else searchTimer = setTimeout(load, 300);
        }
    }

    ui.search.addEventListener("input", () => search());
    document.getElementById("search-form").addEventListener("submit", event => {
        event.preventDefault();
        search(true);
        onViewChange(state.savedOnly ? "library" : "catalog");
    });
    ui.sort.addEventListener("change", event => {
        if (state.savedOnly) {
            state.librarySort = event.target.value;
            render();
        } else {
            clearTimeout(searchTimer);
            onPageChange(1, event.target.value, ui.search.value.trim());
        }
    });
    ui.genres.addEventListener("click", event => {
        const button = event.target.closest("[data-genre]");
        if (!button) return;
        state.genre = button.dataset.genre;
        render();
    });
    ui.reset.addEventListener("click", () => {
        const stayInLibrary = state.savedOnly && favorites.status !== "guest";
        show(stayInLibrary);
        onViewChange(stayInLibrary ? "library" : "catalog");
    });
    ui.retry?.addEventListener("click", () => onPageChange(state.requestedPage, state.requestedSort, state.query));
    ui.retryFavorites?.addEventListener("click", () => favorites.load());
    ui.signIn?.addEventListener("click", () => document.getElementById("account-entry").click());
    favorites.subscribe(() => { if (state.savedOnly) render(); });

    return {
        show,
        setQuery(query) {
            ui.search.value = query;
            search(true);
        },
        showGenre(genre) {
            show(false, genre);
        },
        startLoading(page, sort = state.sort, query = state.query) {
            if (!Number.isInteger(page) || page < 1 || (state.loading && query === state.query)) return false;
            if (query !== state.query) {
                state.query = query;
                state.games = [];
                state.page = 1;
                state.hasNext = false;
                state.loaded = false;
            }
            state.loading = true;
            state.requestedPage = page;
            state.requestedSort = sort;
            state.loadFailed = false;
            render();
            return true;
        },
        setPage(data, sort = state.requestedSort) {
            const pageChanged = state.loaded && state.page !== data.page;
            state.games = data.results;
            state.page = data.page;
            state.sort = sort;
            state.hasNext = data.hasNext;
            state.loaded = true;
            state.loading = false;
            state.loadFailed = false;
            render();
            if (pageChanged && !state.savedOnly) document.getElementById("catalog").scrollIntoView();
        },
        showError() {
            state.loading = false;
            state.loadFailed = true;
            render();
        }
    };
}
