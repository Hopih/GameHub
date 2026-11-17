const genreNames = {
    action: "Экшен",
    "role-playing-games-rpg": "RPG",
    adventure: "Приключения",
    indie: "Инди",
    shooter: "Шутер",
    puzzle: "Головоломка",
    platformer: "Платформер",
    racing: "Гонки",
    strategy: "Стратегия",
    sports: "Спорт",
    casual: "Казуальная",
    simulation: "Симулятор"
};

export function gameUrl(game) {
    return `https://rawg.io/games/${encodeURIComponent(game.slug || game.id)}`;
}

export function genreName(genre) {
    return genre ? (genreNames[genre.slug] || genre.name) : "Открой для себя";
}

export function gameCaption(game) {
    const genres = game.genres || [];
    if (genres.some(genre => genre.slug === "adventure")) {
        return "Для тех, кто любит открывать новые миры.";
    }
    if (genres.some(genre => genre.slug === "indie")) {
        return "Особенная атмосфера и свой взгляд на игры.";
    }
    return "Популярная игра, которую стоит открыть для себя.";
}
