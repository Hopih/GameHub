const dateFormat = new Intl.DateTimeFormat("ru-RU", { dateStyle: "medium" });

export function initSteamNews() {
    const form = document.getElementById("steam-news-search");
    const input = document.getElementById("steam-news-query");
    const games = document.getElementById("steam-news-games");
    const posts = document.getElementById("steam-news-posts");
    const status = document.getElementById("steam-news-status");
    let request;

    form.addEventListener("submit", async event => {
        event.preventDefault();
        const query = input.value.trim();
        if (!query) {
            input.value = "";
            input.reportValidity();
            return;
        }

        games.replaceChildren();
        games.hidden = true;
        const signal = startRequest("Ищем игру в Steam…");
        try {
            const matches = await getResults("/steam/search", { q: query }, signal);
            if (signal.aborted) return;

            const buttons = matches.map(game => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = "filter-button";
                button.textContent = game.name;
                button.setAttribute("aria-pressed", "false");
                button.addEventListener("click", () => loadNews(game, button));
                return button;
            });
            games.replaceChildren(...buttons);
            games.hidden = buttons.length === 0;
            status.textContent = buttons.length ? "Выбери игру." : "В Steam ничего не нашлось. Попробуй другое название.";
            if (buttons.length === 1) void loadNews(matches[0], buttons[0]);
        } catch {
            if (!signal.aborted) status.textContent = "Не удалось найти игры в Steam. Попробуй ещё раз.";
        }
    });

    async function loadNews(game, selectedButton) {
        for (const button of games.children) {
            button.classList.toggle("is-active", button === selectedButton);
            button.setAttribute("aria-pressed", String(button === selectedButton));
        }

        const signal = startRequest(`Загружаем новости для ${game.name}…`);
        try {
            const articles = await getResults("/steam/news", { appid: game.appId }, signal);
            if (signal.aborted) return;
            posts.replaceChildren(...articles.map(createPost));
            posts.hidden = articles.length === 0;
            status.textContent = articles.length ? "" : `Для ${game.name} в Steam пока нет публикаций.`;
        } catch {
            if (!signal.aborted) status.textContent = "Не удалось загрузить новости. Нажми на название игры ещё раз.";
        }
    }

    function startRequest(message) {
        request?.abort();
        request = new AbortController();
        posts.replaceChildren();
        posts.hidden = true;
        status.textContent = message;
        return request.signal;
    }
}

async function getResults(path, parameters, signal) {
    const response = await fetch(`${path}?${new URLSearchParams(parameters)}`, { signal });
    if (!response.ok) throw new Error(`Steam request failed: ${response.status}`);
    const data = await response.json();
    if (!Array.isArray(data.results)) throw new Error("Unexpected Steam response");
    return data.results;
}

function createPost(article) {
    const item = document.createElement("li");
    const link = document.createElement("a");
    link.href = article.url;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    link.textContent = article.title;
    item.append(link);

    const date = new Date(article.publishedAt * 1000);
    if (article.publishedAt > 0 && Number.isFinite(date.getTime())) {
        const time = document.createElement("time");
        time.dateTime = date.toISOString();
        time.textContent = dateFormat.format(date);
        item.append(time);
    }
    return item;
}
