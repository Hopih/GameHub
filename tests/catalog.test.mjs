import assert from "node:assert/strict";
import test from "node:test";
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
    const catalog = createCatalog({ favorites, onViewChange: view => views.push(view) });
    return { catalog, element, favorites, views };
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
    const { catalog, element, favorites, views } = setup(t);
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
});
