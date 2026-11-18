export function bindAccount(session, notify) {
    const entry = document.getElementById("account-entry");
    const logout = document.getElementById("account-logout");
    const dialog = document.getElementById("account-dialog");
    const title = document.getElementById("account-title");
    const close = document.getElementById("account-close");
    const loginForm = document.getElementById("account-login");
    const registerForm = document.getElementById("account-register");
    const switchMode = document.getElementById("account-switch");
    const error = document.getElementById("account-error");
    let registering = false;
    let submitting = false;

    function showForm(showRegister) {
        registering = showRegister;
        title.textContent = registering ? "Регистрация" : "Вход в GameHub";
        loginForm.hidden = registering;
        registerForm.hidden = !registering;
        switchMode.textContent = registering ? "Уже есть аккаунт? Войти" : "Создать аккаунт";
        error.textContent = "";
        if (dialog.open) (registering ? registerForm : loginForm).elements[0].focus();
    }

    function openLogin(message = "") {
        showForm(false);
        error.textContent = message;
        if (!dialog.open) dialog.showModal();
    }

    session.subscribe(profile => {
        entry.textContent = profile?.nickname ?? "Войти";
        entry.title = profile?.nickname ?? "";
        entry.disabled = Boolean(profile);
        logout.hidden = !profile;
        if (profile && dialog.open) dialog.close();
    });

    entry.addEventListener("click", () => openLogin());
    close.addEventListener("click", () => dialog.close());
    switchMode.addEventListener("click", () => showForm(!registering));
    dialog.addEventListener("close", () => {
        loginForm.elements.password.value = "";
        registerForm.elements.password.value = "";
    });
    logout.addEventListener("click", async () => {
        logout.disabled = true;
        try {
            await session.logout();
            entry.focus();
            notify("Ты вышел из аккаунта.");
        } catch (requestError) {
            notify(requestError.message);
        } finally {
            logout.disabled = false;
        }
    });

    async function submit(event) {
        event.preventDefault();
        if (submitting) return;
        const form = event.currentTarget;
        const data = Object.fromEntries(new FormData(form));
        const button = form.querySelector("[type=submit]");
        submitting = true;
        button.disabled = true;
        switchMode.disabled = true;
        error.textContent = "";
        try {
            if (form === registerForm) {
                await session.register(data);
                loginForm.elements.email.value = data.email;
                showForm(false);
                loginForm.querySelector("[type=submit]").disabled = true;
            }
            await session.login({ email: data.email, password: data.password });
            loginForm.reset();
            registerForm.reset();
        } catch (requestError) {
            error.textContent = requestError.message;
        } finally {
            submitting = false;
            button.disabled = false;
            loginForm.querySelector("[type=submit]").disabled = false;
            switchMode.disabled = false;
        }
    }

    loginForm.addEventListener("submit", submit);
    registerForm.addEventListener("submit", submit);
    return openLogin;
}
