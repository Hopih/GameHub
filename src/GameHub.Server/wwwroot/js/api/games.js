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

export async function getGames(page = 1) {
    const data = await getGameList(`/games?page=${page}`);
    if (!Number.isInteger(data.page) || data.page !== page || typeof data.hasNext !== "boolean") {
        throw new Error("Unexpected pagination response");
    }
    return data;
}

export async function getFeaturedGames() {
    const data = await getGameList("/games/featured");
    return data.results;
}
