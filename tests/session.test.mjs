import assert from "node:assert/strict";
import test from "node:test";
import { createSession } from "../src/GameHub.Server/wwwroot/js/api/session.js";

// Две вкладки без Web Locks получают ответы на запрос с одной и той же cookie.
for (const notifyFirst of [true, false]) {
    test(`concurrent refresh recovers when peer notification arrives ${notifyFirst ? "before" : "after"} 401`, async t => {
        const channels = [];
        const notifications = [];
        class TabChannel extends EventTarget {
            constructor() {
                super();
                channels.push(this);
            }
            postMessage(data) {
                for (const peer of channels.filter(channel => channel !== this)) {
                    notifications.push(() => peer.dispatchEvent(new MessageEvent("message", { data })));
                }
            }
        }
        t.mock.getter(globalThis, "navigator", () => ({}));
        const originalWindow = globalThis.window;
        const originalChannel = globalThis.BroadcastChannel;
        globalThis.window = { BroadcastChannel: TabChannel };
        globalThis.BroadcastChannel = TabChannel;
        t.after(() => {
            if (originalWindow === undefined) delete globalThis.window;
            else globalThis.window = originalWindow;
            globalThis.BroadcastChannel = originalChannel;
        });

        const stale = Promise.withResolvers();
        let requests = 0;
        t.mock.method(globalThis, "fetch", async url => {
            if (url === "/auth/me") return Response.json({ id: "player" });
            assert.equal(url, "/auth/refresh");
            if (++requests === 2) return stale.promise;
            return Response.json({ accessToken: "test-token" });
        });
        const first = createSession({ onError: assert.fail });
        const second = createSession({ onError: assert.fail });
        const signedIn = Promise.withResolvers();
        second.subscribe(profile => { if (profile) signedIn.resolve(); });
        const firstRequest = first.restore();
        const secondRequest = second.restore();
        await firstRequest;
        assert.equal(requests, 2);
        if (notifyFirst) notifications.shift()();
        stale.resolve(new Response(null, { status: 401 }));
        await secondRequest;
        if (!notifyFirst) notifications.shift()();
        await signedIn.promise;
        while (notifications.length) notifications.shift()();
        assert.equal(first.profile.id, "player");
        assert.equal(second.profile.id, "player");
        assert.equal(requests, 3);
    });
}
