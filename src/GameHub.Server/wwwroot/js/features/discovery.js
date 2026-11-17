import { renderGameCards } from "../components/game-card.js";
import { setGameImage } from "../shared/dom.js";
import { gameUrl } from "../shared/game-info.js";

export function createDiscovery(favorites) {
    const grid = document.getElementById("featured-games");
    const status = document.getElementById("featured-status");
    const spotlightStatus = document.getElementById("spotlight-status");
    const spotlightRetry = document.getElementById("retry-spotlights");

    return {
        startLoading() {
            status.textContent = "Загружаем игры из каталога…";
            grid.setAttribute("aria-busy", "true");
        },
        setGames(games) {
            renderGameCards(grid, games.slice(0, 4), favorites);
            status.textContent = "Популярные игры из каталога. Твоя следующая любимая может быть среди них.";
        },
        showError() {
            grid.replaceChildren();
            grid.setAttribute("aria-busy", "false");
            status.textContent = "Каталог пока недоступен. Попробуй загрузить его ещё раз ниже.";
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
