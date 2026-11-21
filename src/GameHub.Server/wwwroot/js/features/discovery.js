import { renderGameCards } from "../components/game-card.js";
import { setGameImage } from "../shared/dom.js";
import { gameUrl } from "../shared/game-info.js";

export function createRecommendations(favorites) {
    const grid = document.getElementById("recommended-games");
    const status = document.getElementById("recommended-status");
    const retry = document.getElementById("retry-recommendations");
    let popularGames = null;
    let matches = null;
    let canRender = false;

    function renderRecommendations() {
        if (!canRender) return;
        if (matches) {
            renderGameCards(grid, matches.results, favorites);
            status.textContent = matches.results.length
                ? `Подходящие теги — ${matches.tags.join(", ")}.`
                : "Не нашли подходящих игр. Попробуй описать жанр или игровые механики.";
            return;
        }
        if (!popularGames) return;

        const genreWeights = new Map();
        for (const game of favorites.getGames()) {
            for (const genre of game.genres ?? []) {
                genreWeights.set(genre.slug, (genreWeights.get(genre.slug) ?? 0) + 1);
            }
        }

        const ranked = popularGames
            .filter(game => !favorites.has(game.id))
            .map(game => ({
                game,
                score: (game.genres ?? []).reduce((sum, genre) => sum + (genreWeights.get(genre.slug) ?? 0), 0)
            }))
            .sort((a, b) => b.score - a.score)
            .slice(0, 12);

        renderGameCards(grid, ranked.map(({ game }) => game), favorites);
        if (!ranked.length) status.textContent = "Новых игр в этой подборке пока нет. Загляни в каталог.";
        else if (ranked[0].score > 0) status.textContent = "Сначала — игры в жанрах из твоего избранного.";
        else status.textContent = "Популярные игры из каталога.";
    }

    favorites.subscribe(renderRecommendations);

    return {
        startLoading(byDescription) {
            canRender = false;
            status.textContent = byDescription ? "Подбираем игры по описанию…" : "Загружаем игры из каталога…";
            grid.replaceChildren();
            grid.setAttribute("aria-busy", "true");
            retry.hidden = true;
        },
        setGames(games) {
            matches = null;
            popularGames = games;
            canRender = true;
            renderRecommendations();
        },
        setMatches(data) {
            matches = data;
            canRender = true;
            renderRecommendations();
        },
        showError(byDescription) {
            canRender = false;
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
