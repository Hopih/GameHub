export function createPagination({ onPrevious, onNext }) {
    const container = document.getElementById("catalog-pagination");
    const previous = document.getElementById("previous-page");
    const next = document.getElementById("next-page");
    const number = document.getElementById("page-number");

    previous.addEventListener("click", () => {
        if (!previous.disabled) onPrevious();
    });
    next.addEventListener("click", () => {
        if (!next.disabled) onNext();
    });

    return {
        render({ page, hasNext, disabled, hidden }) {
            container.hidden = hidden;
            number.textContent = `Страница ${page}`;
            previous.disabled = disabled || page === 1;
            next.disabled = disabled || !hasNext;
        }
    };
}
