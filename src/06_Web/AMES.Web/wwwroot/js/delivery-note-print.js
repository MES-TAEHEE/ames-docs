document.addEventListener('click', event => {
    if (!event.target.closest('[data-delivery-note-print]')) return;
    if (!document.querySelector('article.delivery-note')) return;
    window.print();
});
