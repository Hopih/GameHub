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
    const descriptionFields = document.getElementById("recommendation-description-fields");
    const search = document.getElementById("search-recommendations");
    const reset = document.getElementById("reset-recommendations");
    const settings = document.getElementById("recommendation-settings");
    const openSettings = document.getElementById("open-recommendation-settings");
    const cancelSettings = document.getElementById("cancel-recommendation-settings");
    const settingsForm = document.getElementById("recommendation-settings-form");
    const criteria = settingsForm.querySelector("fieldset");
    const saveSettings = settingsForm.querySelector("[type=submit]");
    const settingsError = document.getElementById("recommendation-settings-error");
    const byDescription = document.getElementById("recommendation-sort-description");
    const byPersonal = document.getElementById("recommendation-sort-personal");
    const all = document.getElementById("recommendation-sort-all");
    const defaultSettings = { description: byDescription.defaultChecked, personal: byPersonal.defaultChecked };
    let sortOptions = defaultSettings;
    let accountVersion = 0;
    let activeDescription = "";
    let latestRequest = 0;

    openSettings.addEventListener("click", () => {
        byDescription.checked = sortOptions.description;
        byPersonal.checked = sortOptions.personal;
        updateAll();
        settingsError.hidden = true;
        settings.showModal();
    });
    cancelSettings.addEventListener("click", () => settings.close());
    settings.addEventListener("cancel", event => {
        if (saveSettings.disabled) event.preventDefault();
    });
    byDescription.addEventListener("change", updateAll);
    byPersonal.addEventListener("change", updateAll);
    all.addEventListener("change", () => {
        byDescription.checked = byPersonal.checked = all.checked;
        updateAll();
    });
    settingsForm.addEventListener("submit", async event => {
        event.preventDefault();
        if (saveSettings.disabled) return;
        const next = { description: byDescription.checked, personal: byPersonal.checked };
        if (next.description === sortOptions.description && next.personal === sortOptions.personal) {
            settings.close();
            return;
        }

        const version = accountVersion;
        settingsError.hidden = true;
        setSaving(true);
        try {
            if (session.profile) {
                await session.request("/settings", {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(next)
                });
            }
            if (version !== accountVersion) return;
            settings.close();
            applyOptions(next);
        } catch {
            if (version !== accountVersion) return;
            settingsError.textContent = "Не удалось сохранить настройки. Попробуй ещё раз.";
            settingsError.hidden = false;
        } finally {
            if (version === accountVersion) setSaving(false);
        }
    });

    session.subscribe(async profile => {
        const version = ++accountVersion;
        settings.close();
        setSaving(false);
        openSettings.disabled = Boolean(profile);
        applyOptions(defaultSettings);
        if (!profile) return;

        try {
            const response = await session.request("/settings");
            const saved = await response.json();
            if (version !== accountVersion) return;
            if (typeof saved?.description !== "boolean" || typeof saved?.personal !== "boolean") {
                throw new Error("Неверный формат настроек.");
            }
            applyOptions(saved);
        } catch {
            if (version === accountVersion) notify("Не удалось загрузить настройки рекомендаций.");
        } finally {
            if (version === accountVersion) openSettings.disabled = false;
        }
    });

    function setSaving(saving) {
        criteria.disabled = saveSettings.disabled = cancelSettings.disabled = saving;
    }

    function applyOptions(next) {
        const descriptionChanged = sortOptions.description !== next.description;
        sortOptions = next;
        if (!descriptionChanged) return;
        descriptionFields.hidden = description.disabled = search.hidden = !sortOptions.description;
        void loadRecommendations();
    }

    function updateAll() {
        all.checked = byDescription.checked && byPersonal.checked;
        all.indeterminate = byDescription.checked !== byPersonal.checked;
    }

    form.addEventListener("submit", event => {
        event.preventDefault();
        if (!sortOptions.description) return;
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
        const query = sortOptions.description ? activeDescription : "";
        const request = ++latestRequest;
        reset.hidden = !query;
        recommendations.startLoading(Boolean(query));
        try {
            const data = query ? await getRecommendations(query) : await getGames();
            if (request !== latestRequest) return;
            recommendations.setResults(data);
        } catch (error) {
            if (request !== latestRequest) return;
            console.error("Не удалось загрузить рекомендации:", error);
            recommendations.showError(Boolean(query));
        }
    }
}
