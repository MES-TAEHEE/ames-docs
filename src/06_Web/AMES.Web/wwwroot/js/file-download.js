// Blazor에서 생성한 파일을 브라우저 다운로드로 전달 (CSV 내보내기 등)
window.amesDownload = function (fileName, base64, mimeType) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType || 'application/octet-stream' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
};

// 화면의 SVG 차트(Radzen)를 PNG(base64)로 그린다 — 엑셀 내보내기에 차트를 같이 넣기 위해.
// CSS 로 칠해진 색(눈금 글자·격자선)은 SVG 를 떼어내면 사라지므로 계산된 스타일을 요소마다 인라인으로 옮긴다.
// 배경은 차트가 놓인 카드의 실제 배경색(다크/라이트 테마)으로 채운다.
window.amesCaptureSvgs = async function (selector, scale) {
    scale = scale || 2;
    const props = ["fill", "fill-opacity", "stroke", "stroke-width", "stroke-opacity", "stroke-dasharray", "stroke-linecap", "stroke-linejoin",
                   "opacity", "font-family", "font-size", "font-weight", "text-anchor", "dominant-baseline", "letter-spacing", "visibility"];
    const bgOf = el => { for (let e = el; e; e = e.parentElement) { const c = getComputedStyle(e).backgroundColor; if (c && c !== "transparent" && !/rgba\(\s*\d+,\s*\d+,\s*\d+,\s*0\)/.test(c)) return c; } return "#ffffff"; };
    const out = [];
    // 차트 컨테이너마다 가장 큰 SVG 하나만 — 원형 차트의 범례 아이콘(20×20 svg)이 같이 잡히는 것을 막는다
    const best = new Map();
    for (const svg of document.querySelectorAll(selector)) {
        const key = svg.closest(".rpt-rb-chart, .ames-card") || svg.parentElement; const r = svg.getBoundingClientRect();
        if (!best.has(key) || r.width * r.height > best.get(key).area) best.set(key, { svg, area: r.width * r.height });
    }
    for (const { svg } of best.values()) {
        const rect = svg.getBoundingClientRect(); if (rect.width < 10 || rect.height < 10) continue;
        const clone = svg.cloneNode(true);
        const src = svg.querySelectorAll("*"), dst = clone.querySelectorAll("*");
        for (let i = 0; i < src.length; i++) { const cs = getComputedStyle(src[i]); for (const p of props) { const v = cs.getPropertyValue(p); if (v) dst[i].style.setProperty(p, v); } }
        clone.setAttribute("xmlns", "http://www.w3.org/2000/svg");
        clone.setAttribute("width", rect.width); clone.setAttribute("height", rect.height);
        if (!clone.getAttribute("viewBox")) clone.setAttribute("viewBox", `0 0 ${rect.width} ${rect.height}`);
        const url = URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(clone)], { type: "image/svg+xml;charset=utf-8" }));
        try {
            const img = await new Promise((res, rej) => { const im = new Image(); im.onload = () => res(im); im.onerror = rej; im.src = url; });
            const canvas = document.createElement("canvas");
            canvas.width = Math.round(rect.width * scale); canvas.height = Math.round(rect.height * scale);
            const ctx = canvas.getContext("2d");
            ctx.fillStyle = bgOf(svg); ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
            // Radzen 범례는 SVG 밖 HTML(.rz-legend-item = 10×10 색 견본 svg + 글자)이라 위치 그대로 캔버스에 덧그린다
            for (const item of svg.closest(".rz-chart")?.querySelectorAll(".rz-legend-item") ?? []) {
                const sw = item.querySelector("svg"), txt = item.querySelector(".rz-legend-item-text");
                if (!sw || !txt) continue;
                const sr = sw.getBoundingClientRect(), tr = txt.getBoundingClientRect(), ts = getComputedStyle(txt);
                const shape = sw.querySelector("path, rect, circle, polygon");
                ctx.fillStyle = shape ? getComputedStyle(shape).fill : ts.color;
                ctx.fillRect((sr.left - rect.left) * scale, (sr.top - rect.top) * scale, sr.width * scale, sr.height * scale);
                ctx.fillStyle = ts.color; ctx.font = `${ts.fontWeight} ${parseFloat(ts.fontSize) * scale}px ${ts.fontFamily}`; ctx.textBaseline = "middle";
                ctx.fillText(txt.innerText.trim(), (tr.left - rect.left) * scale, (tr.top + tr.height / 2 - rect.top) * scale);
            }
            const head = svg.closest(".rpt-rb-chart, .ames-card")?.querySelector(".h, .ames-card-h");
            out.push({ title: (head?.innerText || "").trim(), png: canvas.toDataURL("image/png").split(",")[1] });
        } catch (e) { console.warn("amesCaptureSvgs: skip", e); }
        finally { URL.revokeObjectURL(url); }
    }
    return out;
};
