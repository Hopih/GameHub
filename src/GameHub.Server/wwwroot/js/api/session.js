const channelName = "gamehub-session";

function post(url, data) {
    return fetch(url, {
        method: "POST",
        headers: data === undefined ? undefined : { "Content-Type": "application/json" },
        body: data === undefined ? undefined : JSON.stringify(data)
    });
}

async function requireSuccess(response) {
    if (response.ok) return response;
    const details = response.status < 500 ? await response.text() : "";
    throw new Error(details || "Сервер временно недоступен. Попробуй ещё раз.");
}

export function createSession({ onSignInRequired, onError }) {
    const listeners = new Set();
    const channel = "BroadcastChannel" in window ? new BroadcastChannel(channelName) : null;
    let accessToken = null;
    let profile = null;
    let revision = 0;
    let refreshing = null;
    let sessionQueue = Promise.resolve();

    function setSession(nextProfile, nextToken) {
        const accountChanged = profile?.id !== nextProfile?.id;
        if (accountChanged) revision++;
        profile = nextProfile;
        accessToken = nextToken;
        if (accountChanged) listeners.forEach(listener => listener(profile));
    }

    function withSessionLock(action) {
        if (navigator.locks) return navigator.locks.request(channelName, action);
        const task = sessionQueue.then(action);
        sessionQueue = task.catch(() => {});
        return task;
    }

    function bearerRequest(url, token, options = {}) {
        const headers = new Headers(options.headers);
        headers.set("Authorization", `Bearer ${token}`);
        return fetch(url, { ...options, headers });
    }

    async function readSession(response, startedAt = revision) {
        await requireSuccess(response);
        const { accessToken: token } = await response.json();
        const me = await requireSuccess(await bearerRequest("/auth/me", token));
        const nextProfile = await me.json();
        if (revision === startedAt) setSession(nextProfile, token);
    }

    function restore() {
        if (refreshing) return refreshing;

        const startedAt = revision;
        refreshing = withSessionLock(async () => {
            if (revision !== startedAt) return;
            const response = await post("/auth/refresh");
            if (revision !== startedAt) return;
            if (response.status === 401) {
                setSession(null, null);
                return;
            }
            await readSession(response, startedAt);
        }).finally(() => { refreshing = null; });
        return refreshing;
    }

    channel?.addEventListener("message", event => {
        if (event.data !== "login" && event.data !== "logout") return;
        revision++;
        setSession(null, null);
        if (event.data === "login") {
            // Дождаться предыдущего refresh и прочитать cookie текущего аккаунта.
            void (refreshing ?? Promise.resolve()).catch(() => {}).then(restore).catch(onError);
        }
    });

    return {
        get profile() { return profile; },
        subscribe(listener) {
            listeners.add(listener);
            return () => listeners.delete(listener);
        },
        restore,
        async register(data) {
            await requireSuccess(await post("/register", data));
        },
        async login(data) {
            await withSessionLock(async () => {
                await readSession(await post("/login", data));
                channel?.postMessage("login");
            });
        },
        async logout() {
            await withSessionLock(async () => {
                await requireSuccess(await post("/auth/logout"));
                setSession(null, null);
                channel?.postMessage("logout");
            });
        },
        async request(url, options) {
            const account = profile?.id;
            const startedAt = revision;
            const sentToken = accessToken;
            if (!sentToken) throw new Error("Сначала войди в аккаунт.");

            let response = await bearerRequest(url, sentToken, options);
            if (response.status === 401 && revision === startedAt) {
                if (sentToken === accessToken) await restore();
                if (profile?.id === account && revision === startedAt && accessToken) {
                    response = await bearerRequest(url, accessToken, options);
                }
                if (response.status === 401 && (revision === startedAt || profile === null)) {
                    setSession(null, null);
                    onSignInRequired("Сессия закончилась. Войди снова.");
                }
            }
            return requireSuccess(response);
        }
    };
}
