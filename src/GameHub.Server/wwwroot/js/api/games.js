async function getGameList(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`Games request failed: ${response.status}`);

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
