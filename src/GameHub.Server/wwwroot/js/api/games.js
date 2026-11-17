async function getGameList(url) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`Games request failed: ${response.status}`);

    const data = await response.json();
    if (!Array.isArray(data.results) || !data.results.every(game =>
        Number.isInteger(game.id) && typeof game.name === "string")) {
        throw new Error("Unexpected games response");
    }

    return data.results;
}

export function getGames() {
    return getGameList("/games");
}

export function getFeaturedGames() {
    return getGameList("/games/featured");
}
