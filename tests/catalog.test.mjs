import assert from "node:assert/strict";
import test from "node:test";
import { getGames } from "../src/GameHub.Server/wwwroot/js/api/games.js";
import { createCatalog } from "../src/GameHub.Server/wwwroot/js/features/catalog.js";

function setup(t) {
    const elements = new Map();
    function element(id) {
        if (!elements.has(id)) {
            const node = Object.assign(new EventTarget(), {
                children: [], classList: { toggle() {} }, setAttribute() {},
                querySelectorAll() { return []; },
                replaceChildren(...children) { this.children = children; }
            });
            elements.set(id, node);
        }
        return elements.get(id);
    }
    globalThis.document = { getElementById: element, querySelector: element };
    t.after(() => { delete globalThis.document; });
    const favorites = { getGames: () => [], status: "ready", subscribe() {} };
    const views = [];
    const requests = [];
    const catalog = createCatalog({
        favorites,
        onViewChange: view => views.push(view),
        onPageChange: (...request) => requests.push(request)
    });
    return { catalog, element, favorites, views, requests };
}

test("catalog failure stays in the catalog across navigation and retry", t => {
    const { catalog, element } = setup(t);
    catalog.startLoading(1);
    catalog.show(true);
    catalog.showError();
    assert.equal(element("load-error").hidden, true);
    assert.equal(element("catalog-title").textContent, "Отмеченные игры");

    catalog.show(false);
    assert.equal(element("load-error").hidden, false);
    assert.equal(element("catalog-title").textContent, "Каталог игр");
    catalog.show(true);
    assert.equal(element("load-error").hidden, true);
    catalog.show(false);
    catalog.startLoading(1);
    catalog.setPage({ page: 1, results: [], hasNext: false });
    assert.equal(element("load-error").hidden, true);
    assert.equal(element("empty-state").hidden, false);
});

test("reset keeps a signed-in player in the library and sends a guest to the catalog", t => {
    const { catalog, element, favorites, views, requests } = setup(t);
    catalog.show(true);
    const search = element("game-search");
    search.value = "нет совпадений";
    search.dispatchEvent(new Event("input"));
    element("reset-filters").dispatchEvent(new Event("click"));
    assert.equal(search.value, "");
    assert.equal(views.at(-1), "library");
    assert.equal(element("catalog-title").textContent, "Отмеченные игры");

    favorites.status = "guest";
    catalog.show(true);
    element("reset-filters").dispatchEvent(new Event("click"));
    assert.equal(views.at(-1), "catalog");
    assert.equal(element("catalog-title").textContent, "Каталог игр");
    assert.deepEqual(requests, []);
});

test("catalog search starts on page one and keeps the query for pagination and retry", t => {
    const { catalog, element, requests } = setup(t);
    catalog.startLoading(2, "rating");

    const search = element("game-search");
    search.value = "  Asterix & Obelix  ";
    search.dispatchEvent(new Event("input"));
    assert.deepEqual(requests, []);
    element("search-form").dispatchEvent(new Event("submit"));
    assert.deepEqual(requests, [[1, "rating", "Asterix & Obelix"]]);
    assert.equal(catalog.startLoading(...requests.at(-1)), true);
    catalog.setPage({ page: 1, results: [], hasNext: true });

    element("next-page").dispatchEvent(new Event("click"));
    assert.deepEqual(requests.at(-1), [2, "rating", "Asterix & Obelix"]);
    catalog.startLoading(...requests.at(-1));
    catalog.showError();
    element("retry-load").dispatchEvent(new Event("click"));
    assert.deepEqual(requests.at(-1), [2, "rating", "Asterix & Obelix"]);

    search.value = "";
    search.dispatchEvent(new Event("input"));
    assert.deepEqual(requests.at(-1), [1, "rating", ""]);
});

test("games API sends the complete search query and leaves it out when empty", async t => {
    const fetch = t.mock.method(globalThis, "fetch", async url => {
        const { searchParams } = new URL(url, "http://localhost");
        return Response.json({
            page: Number(searchParams.get("page")),
            ordering: searchParams.get("ordering"),
            hasNext: false,
            results: []
        });
    });
    const query = "Герои & Магия + #2";
    await getGames(2, "rating", query);
    await getGames();

    const [searchRequest, catalogRequest] = fetch.mock.calls.map(({ arguments: [url] }) =>
        new URL(url, "http://localhost").searchParams);
    assert.equal(searchRequest.get("search"), query);
    assert.equal(searchRequest.get("page"), "2");
    assert.equal(searchRequest.get("ordering"), "-metacritic");
    assert.equal(catalogRequest.has("search"), false);
});
