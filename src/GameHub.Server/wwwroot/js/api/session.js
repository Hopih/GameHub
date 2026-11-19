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
    let peerRefreshes = 0;
    let waitingForPeerRefresh = false;

    function announce(event) {
        if (channel) {
            channel.postMessage(event);
        } else {
            // Событие storage получают остальные вкладки. Токены остаются в памяти и cookie.
            try {
                localStorage.setItem(channelName, event);
                localStorage.removeItem(channelName);
            } catch { /* Хранилище может быть отключено в браузере. */ }
        }
    }

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
            while (revision === startedAt) {
                const beforeRequest = peerRefreshes;
                const response = await post("/auth/refresh");
                if (revision !== startedAt) return;
                if (response.status !== 401) {
                    if (response.ok && !navigator.locks) announce("refreshed");
                    waitingForPeerRefresh = false;
                    await readSession(response, startedAt);
                    return;
                }

                // Другая вкладка могла сменить cookie, пока наш запрос был в пути.
                if (beforeRequest !== peerRefreshes) continue;
                waitingForPeerRefresh = !navigator.locks;
                setSession(null, null);
                return;
            }
        }).finally(() => { refreshing = null; });
        return refreshing;
    }

    function afterCurrentRefresh() {
        const startedAt = revision;
        void (refreshing ?? Promise.resolve()).catch(() => {}).then(() => {
            if (revision === startedAt) return restore();
        }).catch(onError);
    }

    function onPeerEvent(event) {
        if (event === "refreshed") {
            peerRefreshes++;
            if (waitingForPeerRefresh) {
                waitingForPeerRefresh = false;
                afterCurrentRefresh();
            }
            return;
        }
        if (event !== "login" && event !== "logout") return;
        waitingForPeerRefresh = false;
        revision++;
        setSession(null, null);
        if (event === "login") afterCurrentRefresh();
    }

    if (channel) channel.addEventListener("message", event => onPeerEvent(event.data));
    else window.addEventListener("storage", event => {
        if (event.key === channelName) onPeerEvent(event.newValue);
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
                waitingForPeerRefresh = false;
                announce("login");
            });
        },
        async logout() {
            await withSessionLock(async () => {
                await requireSuccess(await post("/auth/logout"));
                setSession(null, null);
                waitingForPeerRefresh = false;
                announce("logout");
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
