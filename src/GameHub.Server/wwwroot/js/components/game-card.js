import { createIcon, setGameImage } from "../shared/dom.js";
import { gameUrl, genreName, gameCaption } from "../shared/game-info.js";

export function updateFavoriteButton(button, selected) {
    button.classList.toggle("is-selected", selected);
    button.setAttribute("aria-pressed", String(selected));
    button.setAttribute("aria-label",
        `${selected ? "Снять отметку" : "Отметить игру"}: ${button.dataset.gameName}`);
}

export function createGameCard(game, selected = false) {
    const card = document.createElement("article");
    card.className = "game-card";
    const cover = document.createElement("div");
    cover.className = "game-card__cover";
    const image = document.createElement("img");
    image.loading = "lazy";
    image.decoding = "async";
    setGameImage(image, game.background_image, game.name);
    cover.append(image);

    if (Number(game.rating) > 0) {
        const rating = document.createElement("span");
        rating.className = "game-card__rating";
        rating.setAttribute("aria-label", `Рейтинг RAWG: ${game.rating} из 5`);
        rating.append(createIcon("star"), Number(game.rating).toFixed(1));
        cover.append(rating);
    }

    const body = document.createElement("div");
    body.className = "game-card__body";
    const titleRow = document.createElement("div");
    titleRow.className = "game-card__title-row";
    const title = document.createElement("h3");
    const link = document.createElement("a");
    link.href = gameUrl(game);
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    link.textContent = game.name;
    link.setAttribute("aria-label", `${game.name} — открыть на RAWG`);
    title.append(link);

    const favorite = document.createElement("button");
    favorite.type = "button";
    favorite.className = "favorite-button";
    favorite.dataset.favoriteId = game.id;
    favorite.dataset.gameName = game.name;
    favorite.append(createIcon("heart"));
    updateFavoriteButton(favorite, selected);
    favorite.addEventListener("click", () => {
        favorite.dispatchEvent(new CustomEvent("favorite-toggle", { bubbles: true, detail: game }));
    });
    titleRow.append(title, favorite);

    const meta = document.createElement("p");
    meta.className = "game-card__meta";
    const genre = document.createElement("span");
    genre.textContent = genreName(game.genres?.[0]);
    meta.append(genre);
    if (game.released) {
        const year = document.createElement("span");
        year.textContent = game.released.slice(0, 4);
        meta.append(year);
    }

    const reason = document.createElement("p");
    reason.className = "game-card__reason";
    reason.textContent = gameCaption(game);
    body.append(titleRow, meta, reason);
    card.append(cover, body);
    return card;
}

export function renderGameCards(container, games, favorites) {
    container.replaceChildren(...games.map(game => createGameCard(game, favorites.has(game.id))));
    container.setAttribute("aria-busy", "false");
}
