export function createNotifier() {
    const notification = document.getElementById("notification");
    let timer;

    return message => {
        notification.textContent = message;
        notification.classList.add("is-visible");
        clearTimeout(timer);
        timer = setTimeout(() => notification.classList.remove("is-visible"), 2300);
    };
}
