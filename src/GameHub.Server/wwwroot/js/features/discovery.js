import { renderGameCards } from "../components/game-card.js";
import { setGameImage } from "../shared/dom.js";
import { gameUrl } from "../shared/game-info.js";

export function createRecommendations(favorites) {
    const grid = document.getElementById("recommended-games");
    const status = document.getElementById("recommended-status");
    const retry = document.getElementById("retry-recommendations");
    let selection = null;

    function renderRecommendations() {
        if (!selection) return;
        const { results, tags } = selection;
        const games = tags ? results : results.filter(game => !favorites.has(game.id)).slice(0, 12);

        renderGameCards(grid, games, favorites);
        if (tags) {
            status.textContent = games.length
                ? (tags.length ? `Подходящие теги — ${tags.join(", ")}.` : "Игры по твоему описанию.")
                : "Не нашли подходящих игр. Попробуй описать жанр или игровые механики.";
        } else {
            status.textContent = games.length
                ? "Популярные игры из каталога."
                : "Новых игр в этой подборке пока нет. Загляни в каталог.";
        }
    }

    favorites.subscribe(renderRecommendations);

    return {
        startLoading(byDescription) {
            selection = null;
            status.textContent = byDescription ? "Подбираем игры по описанию…" : "Загружаем игры из каталога…";
            grid.replaceChildren();
            grid.setAttribute("aria-busy", "true");
            retry.hidden = true;
        },
        setResults(data) {
            selection = data;
            renderRecommendations();
        },
        showError(byDescription) {
            selection = null;
            grid.setAttribute("aria-busy", "false");
            status.textContent = byDescription
                ? "Поиск по описанию временно недоступен. Попробуй ещё раз."
                : "Не удалось загрузить рекомендации.";
            retry.hidden = false;
        }
    };
}

export function createDiscovery(favorites) {
    const grid = document.getElementById("featured-games");
    const status = document.getElementById("featured-status");
    const spotlightStatus = document.getElementById("spotlight-status");
    const spotlightRetry = document.getElementById("retry-spotlights");

    return {
        setGames(games) {
            renderGameCards(grid, games.slice(0, 4), favorites);
            status.textContent = "Популярные игры из каталога. Твоя следующая любимая может быть среди них.";
        },
        showError() {
            grid.replaceChildren();
            grid.setAttribute("aria-busy", "false");
            status.textContent = "Не удалось загрузить рекомендации.";
        },
        startSpotlightLoading() {
            spotlightStatus.textContent = "Загружаем обложки из RAWG…";
            spotlightRetry.hidden = true;
            spotlightRetry.disabled = true;
        },
        setSpotlights(games) {
            const hero = games.find(game => game.slug === "elden-ring");
            if (!hero) throw new Error("Featured game is missing");

            setGameImage(document.getElementById("hero-art"), hero.background_image);
            const caption = document.getElementById("hero-caption");
            caption.href = gameUrl(hero);
            caption.querySelector("[data-game-name]").textContent = hero.name.toUpperCase();
            caption.hidden = false;

            document.querySelectorAll("[data-spotlight]").forEach(article => {
                const game = games.find(item => item.slug === article.dataset.spotlight);
                if (!game) return;
                setGameImage(article.querySelector("img"), game.background_image, game.name);
                article.querySelector(".story-tag").textContent = game.name.toUpperCase();
            });
            spotlightStatus.textContent = "";
            spotlightRetry.hidden = true;
        },
        showSpotlightError() {
            spotlightStatus.textContent = "Обложки подборки пока недоступны. Каталог работает независимо от них.";
            spotlightRetry.hidden = false;
            spotlightRetry.disabled = false;
        }
    };
}
