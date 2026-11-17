export function createIcon(name) {
    const icon = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    icon.classList.add("icon");
    icon.setAttribute("aria-hidden", "true");

    const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
    use.setAttribute("href", `#icon-${name}`);
    icon.append(use);
    return icon;
}

export function setGameImage(image, url, alt = "") {
    image.alt = alt;
    image.hidden = !url;
    if (!url) {
        image.removeAttribute("src");
        return;
    }

    // При ошибке остаётся фон блока вместо чужой обложки или битого изображения.
    image.addEventListener("error", () => { image.hidden = true; }, { once: true });
    image.src = url;
}
