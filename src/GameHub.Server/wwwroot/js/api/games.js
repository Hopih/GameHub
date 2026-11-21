async function getGameList(url, options) {
    const response = await fetch(url, options);
    if (!response.ok) {
        const message = options?.method === "POST" ? await response.text() : "";
        throw new Error(message || `Games request failed: ${response.status}`);
    }

    const data = await response.json();
    if (!Array.isArray(data.results) || !data.results.every(game =>
        Number.isInteger(game.id) && typeof game.name === "string")) {
        throw new Error("Unexpected games response");
    }

    return data;
}

const orderings = { popular: "-added", newest: "-released", rating: "-metacritic", name: "name" };

export async function getGames(page = 1, sort = "popular", query = "") {
    const ordering = orderings[sort];
    if (!ordering) throw new Error("Unknown game sort");
    const parameters = new URLSearchParams({ page: String(page), ordering });
    if (query) parameters.set("search", query);
    const data = await getGameList(`/games?${parameters}`);
    if (!Number.isInteger(data.page) || data.page !== page || typeof data.hasNext !== "boolean"
        || data.ordering !== ordering) {
        throw new Error("Unexpected pagination response");
    }
    return data;
}

export async function getFeaturedGames() {
    const data = await getGameList("/games/featured");
    return data.results;
}

export async function getRecommendations(description) {
    const data = await getGameList("/games/recommendations", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ description })
    });
    if (!Array.isArray(data.tags) || !data.tags.every(tag => typeof tag === "string")) {
        throw new Error("Unexpected recommendation tags");
    }
    return data;
}
