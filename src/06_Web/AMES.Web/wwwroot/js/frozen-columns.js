// Radzen 고정(Frozen) 컬럼 sticky 오프셋 보정.
// Radzen은 오프셋(inset-inline-start)을 "선언 폭" 합으로 인라인 지정하지만,
// 테이블이 auto 레이아웃이라 실제 렌더 폭은 뷰포트·줌·배율에 따라 달라져
// 고정 셀 사이에 틈(뒤 컬럼 비침)이 생긴다. 실제 폭을 측정해 오프셋을
// 재계산하고, 소수 배율에서의 레이어 경계 서브픽셀 블렌딩 실선까지 막기 위해
// 두 번째 이후 셀을 2px 겹친다. 측정 기반이라 어떤 줌/배율에서도 성립한다.
(function () {
    const OVERLAP = 2;

    function fixTable(table) {
        for (const row of table.rows) {
            let left = 0, i = 0;
            for (const cell of row.cells) {
                if (!cell.classList.contains('rz-frozen-cell')) break;
                const want = i === 0 ? '0px' : (left - OVERLAP).toFixed(2) + 'px';
                if (cell.style.insetInlineStart !== want) cell.style.insetInlineStart = want;
                left += cell.getBoundingClientRect().width;
                i++;
            }
        }
    }

    let raf = 0;
    function scheduleFix() {
        if (raf) return;
        raf = requestAnimationFrame(function () {
            raf = 0;
            document.querySelectorAll('.ames-rz-grid table').forEach(function (t) {
                if (t.querySelector('.rz-frozen-cell')) fixTable(t);
            });
        });
    }

    // Blazor 재렌더·가상화 행 교체(childList)와 컬럼 리사이즈(style) 모두 감지.
    // 자체 style 쓰기도 이벤트를 내지만 재계산 결과가 같으면 쓰지 않으므로 수렴한다.
    new MutationObserver(scheduleFix).observe(document.body, {
        childList: true, subtree: true, attributes: true, attributeFilter: ['style'],
    });
    window.addEventListener('resize', scheduleFix);
    scheduleFix();
})();
