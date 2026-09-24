// Invoke print directly from the user's click, without a Blazor/DB round trip.
// Some embedded browsers do not implement a print dialog. A self-contained
// HTML download lets the user print the same authorized document externally.
(() => {
    const message = text => {
        const target = document.querySelector('[data-delivery-note-status]');
        if (target) target.textContent = text;
    };
    document.addEventListener('click', event => {
        const button = event.target.closest('[data-delivery-note-print], [data-delivery-note-download]');
        if (!button || !document.querySelector('article.delivery-note')) return;
        if (button.hasAttribute('data-delivery-note-print')) {
            message('인쇄창이 열리지 않으면 인쇄용 파일을 내려받아 Chrome 또는 Edge에서 열어 주세요.');
            try { window.print(); }
            catch { message('이 브라우저에서는 인쇄창을 열 수 없습니다. 인쇄용 파일 내려받기를 이용해 주세요.'); }
            return;
        }
        try {
            const source = document.querySelector('article.delivery-note');
            const copy = source.cloneNode(true);
            // Copy only document styles, never application scripts, cookies or navigation.
            const properties = ['display','box-sizing','color','background-color','font-family','font-size',
                'font-weight','font-style','line-height','letter-spacing','text-align','white-space',
                'overflow-wrap','border-top','border-right','border-bottom','border-left','border-collapse',
                'padding-top','padding-right','padding-bottom','padding-left','margin-top','margin-right',
                'margin-bottom','margin-left','width','max-width','height','grid-template-columns','gap',
                'justify-content','fill'];
            const originals = [source, ...source.querySelectorAll('*')];
            const copies = [copy, ...copy.querySelectorAll('*')];
            originals.forEach((element, index) => {
                const style = getComputedStyle(element);
                properties.forEach(p => copies[index].style.setProperty(p, style.getPropertyValue(p)));
            });
            const doc = document.implementation.createHTMLDocument(button.dataset.deliveryNoteDownload);
            const charset = doc.createElement('meta'); charset.setAttribute('charset','utf-8'); doc.head.prepend(charset);
            const style = doc.createElement('style');
            style.textContent = `body{margin:0;background:white;color:black} .delivery-note{width:210mm!important;max-width:100%!important;margin:16px auto!important} .print-tools{text-align:center;padding:16px} @page{size:A4;margin:12mm} @media print{.print-tools{display:none} .delivery-note{width:100%!important;max-width:none!important;margin:0!important;padding:0!important} thead{display:table-header-group!important} tr,.signatures{break-inside:avoid} table{width:100%!important} h1{outline:none!important}}`;
            doc.head.append(style);
            const toolbar = doc.createElement('div'); toolbar.className='print-tools';
            const print = doc.createElement('button'); print.type='button'; print.textContent='인쇄 / PDF 저장';
            print.setAttribute('onclick','window.print()'); toolbar.append(print);
            doc.body.append(toolbar, doc.importNode(copy,true));
            const url = URL.createObjectURL(new Blob(['<!doctype html>\n',doc.documentElement.outerHTML],{type:'text/html;charset=utf-8'}));
            const a = document.createElement('a'); a.href=url;
            a.download=(button.dataset.deliveryNoteDownload || 'delivery-note').replace(/[^a-zA-Z0-9_-]/g,'_')+'.html';
            document.body.append(a); a.click(); a.remove();
            setTimeout(()=>URL.revokeObjectURL(url),60000);
            message('인쇄용 HTML 파일 다운로드를 요청했습니다. 저장한 파일을 Chrome 또는 Edge에서 열어 인쇄하거나 PDF로 저장해 주세요.');
        } catch { message('파일을 준비하지 못했습니다. 화면을 새로고침한 뒤 다시 시도해 주세요.'); }
    });
})();
