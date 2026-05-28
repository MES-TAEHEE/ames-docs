// ════════════════════════════════════════════════════════════════════════
//  A-MES ERD · Module-detail renderer (v2 with hover highlighting)
//  Improvements over v1:
//   - 2x spacing between tables (cleaner visual breathing room)
//   - Smart edge selection (source picks edge nearest to target)
//   - Default FK line opacity 0.18, hover-highlighted line opacity 0.95
//   - On table hover: show only that table's FK lines + ext chips
//   - Slight hue variation per FK to distinguish overlapping lines
// ════════════════════════════════════════════════════════════════════════
(function () {
  if (typeof ERD_DATA === 'undefined') {
    console.error('ERD_DATA not loaded');
    return;
  }

  const MOD_COLOR = {
    MD:'#f59e0b', WH:'#06b6d4', PP:'#10b981', PR:'#3b82f6',
    PNT:'#f97316', QC:'#ef4444', FG:'#22c55e', MNT:'#0ea5e9',
    SYS:'#64748b', ID:'#a855f7'
  };

  function modOf(tblName) {
    if (tblName.startsWith('AspNet')) return 'ID';
    if (tblName.startsWith('tbl_'))   return 'PR';
    if (tblName.startsWith('PNT_'))   return 'PNT';
    if (tblName.startsWith('PR_'))    return 'PR';
    const prefix = tblName.split('_')[0];
    return MOD_COLOR[prefix] ? prefix : '?';
  }

  function escapeXml(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  // Render a single table box. Returns {svg, anchors} where anchors are
  // candidate connection points for FK source/target.
  function renderTableBox(tbl, x, y, w, color) {
    const HDR_H = 32;
    const ROW_H = 15;
    const h = HDR_H + tbl.cols.length * ROW_H + 8;
    const sid = tbl.name.replace(/[^a-zA-Z0-9_]/g, '_');
    let s = '';
    s += `<g class="tbl-box" data-tbl="${tbl.name}" data-sid="${sid}">`;
    s += `<rect class="tbl-bg" x="${x}" y="${y}" width="${w}" height="${h}" rx="5" fill="#0f1a28" stroke="${color}" stroke-width="1.2"/>`;
    s += `<rect class="tbl-hdr" x="${x}" y="${y}" width="${w}" height="${HDR_H}" rx="5" fill="${color}22"/>`;
    s += `<rect x="${x}" y="${y + HDR_H - 6}" width="${w}" height="6" fill="${color}22"/>`;
    s += `<text x="${x + 10}" y="${y + 15}" font-family="Syne,sans-serif" font-weight="700" font-size="12" fill="#e2eaf8">${escapeXml(tbl.name)}</text>`;
    if (tbl.ko) s += `<text x="${x + 10}" y="${y + 27}" font-family="JetBrains Mono,monospace" font-weight="700" font-size="9" fill="${color}">${escapeXml(tbl.ko)}</text>`;

    const colAnchors = []; // per-column anchors: {col, y, flag, target}
    tbl.cols.forEach((c, i) => {
      const [name, type, flag] = c;
      const ry = y + HDR_H + 4 + i * ROW_H;
      const icon = flag === 'PK' ? '◆' : (flag.startsWith('FK') ? '◇' : '·');
      const cls = flag === 'PK' ? 'pk' : (flag.startsWith('FK') ? 'fk' : '');
      const iconColor = flag === 'PK' ? '#fbbf24' : (flag.startsWith('FK') ? '#a5b4fc' : '#5a7088');
      const nameColor = flag === 'PK' ? '#fde047' : (flag.startsWith('FK') ? '#c7d2fe' : '#9ab1c8');
      s += `<text x="${x + 8}" y="${ry + 11}" font-family="JetBrains Mono,monospace" font-size="9.5" fill="${iconColor}" font-weight="700">${icon}</text>`;
      s += `<text x="${x + 22}" y="${ry + 11}" font-family="JetBrains Mono,monospace" font-size="9.5" fill="${nameColor}" font-weight="${flag === 'PK' ? '700' : '500'}">${escapeXml(name)}</text>`;
      s += `<text x="${x + w - 8}" y="${ry + 11}" font-family="JetBrains Mono,monospace" font-size="9" fill="#5a7088" text-anchor="end">${escapeXml(type)}</text>`;
      colAnchors.push({col: name, y: ry + 7, flag, target: flag.startsWith('FK') ? flag.substring(3) : null});
    });
    s += `</g>`;
    return { svg: s, h, colAnchors };
  }

  // Layout tables using shortest-column-first flow + spacing
  function layoutTables(tables, totalW, cols) {
    const COL_GAP = 36;   // wider gap between cols
    const ROW_GAP = 28;   // wider gap between rows
    const SIDE_PAD = 30;
    const colW = Math.floor((totalW - SIDE_PAD * 2 - COL_GAP * (cols - 1)) / cols);
    const colTops = new Array(cols).fill(30);
    const positions = [];
    tables.forEach(t => {
      let ci = 0;
      for (let i = 1; i < cols; i++) if (colTops[i] < colTops[ci]) ci = i;
      const x = SIDE_PAD + ci * (colW + COL_GAP);
      const y = colTops[ci];
      const h = 32 + t.cols.length * 15 + 8;
      positions.push({ t, x, y, w: colW, h, colIdx: ci });
      colTops[ci] = y + h + ROW_GAP;
    });
    return { positions, totalH: Math.max(...colTops) + 30, colW };
  }

  // Smart edge selection: pick edges to minimize line travel
  function pickEdges(src, dst) {
    const srcCx = src.x + src.w / 2;
    const dstCx = dst.x + dst.w / 2;
    let sx, tx;
    if (dstCx > srcCx) {
      sx = src.x + src.w;  // source right edge
      tx = dst.x;          // target left edge
    } else {
      sx = src.x;          // source left edge
      tx = dst.x + dst.w;  // target right edge
    }
    return { sx, tx };
  }

  function drawRelations(positions, anchors, modCode) {
    const tblIndex = {};
    positions.forEach(p => { tblIndex[p.t.name] = p; });
    let lines = '';
    const externals = [];
    const baseColor = MOD_COLOR[modCode] || '#94a3b8';

    anchors.forEach((a, idx) => {
      const srcBox = tblIndex[a.srcTbl];
      const dst = tblIndex[a.dstTbl];
      if (dst && srcBox) {
        // Internal FK
        const { sx, tx } = pickEdges(srcBox, dst);
        const sy = a.srcY;
        // For destination Y, target the row of the FK column (usually PK at top)
        const dstPkRow = dst.t.cols.findIndex(c => c[2] === 'PK');
        const dstColIdx = dstPkRow >= 0 ? dstPkRow : 0;
        const ty = dst.y + 32 + 4 + dstColIdx * 15 + 7;

        // Bezier control points — depending on edge directions
        const dx = Math.abs(tx - sx);
        const offset = Math.min(80, dx / 2 + 20);
        const c1x = sx + (tx > sx ? offset : -offset);
        const c2x = tx + (sx > tx ? offset : -offset);

        // Slight hue variation per FK to distinguish overlapping lines
        const hueOffset = (idx * 23) % 60 - 30;  // -30 to +30 degrees
        const opacity = 0.22;
        lines += `<path class="rel-line" data-src="${srcBox.t.name.replace(/[^a-zA-Z0-9_]/g, '_')}" d="M ${sx} ${sy} C ${c1x} ${sy}, ${c2x} ${ty}, ${tx} ${ty}" stroke="${baseColor}" stroke-width="1.4" fill="none" opacity="${opacity}" style="filter:hue-rotate(${hueOffset}deg)"/>`;
      } else {
        // External FK — render chip
        const targetMod = modOf(a.dstTbl);
        externals.push({ src: srcBox, srcX: a.srcX, srcY: a.srcY, dstTbl: a.dstTbl, dstMod: targetMod });
      }
    });

    // External chips at FK row right edge
    externals.forEach(e => {
      if (!e.src) return;
      const chipX = e.src.x + e.src.w + 4;
      const chipY = e.srcY - 7;
      const text = `→ ${e.dstTbl}`;
      const w = text.length * 5.5 + 12;
      const dstColor = MOD_COLOR[e.dstMod] || '#94a3b8';
      lines += `<g class="ext-chip" data-src="${e.src.t.name.replace(/[^a-zA-Z0-9_]/g, '_')}">`;
      lines += `<rect x="${chipX}" y="${chipY}" width="${w}" height="13" rx="2" fill="#1a2535" stroke="${dstColor}" stroke-width="1" stroke-dasharray="2,2" opacity="0.5"/>`;
      lines += `<text x="${chipX + 4}" y="${chipY + 9}" font-size="8" fill="${dstColor}" font-family="JetBrains Mono,monospace" font-weight="700" opacity="0.7">${escapeXml(text)}</text>`;
      lines += `</g>`;
    });

    return lines;
  }

  function renderModule(modCode, container) {
    const mod = ERD_DATA[modCode];
    if (!mod || !mod.tables || mod.tables.length === 0) {
      container.innerHTML = `<div style="color:#5a7088;padding:40px;text-align:center;font-style:italic">No tables for ${modCode}.</div>`;
      return;
    }
    const color = MOD_COLOR[modCode] || '#94a3b8';
    const W = 1480;
    const COLS = mod.cols || 4;

    const { positions, totalH } = layoutTables(mod.tables, W, COLS);

    // Collect anchors per table (FK source rows)
    const anchors = [];
    let tablesSvg = '';
    positions.forEach(p => {
      const { svg: boxSvg, colAnchors } = renderTableBox(p.t, p.x, p.y, p.w, color);
      tablesSvg += boxSvg;
      colAnchors.forEach(ca => {
        if (ca.flag.startsWith('FK')) {
          const [tName] = ca.target.split('.');
          anchors.push({
            srcTbl: p.t.name,
            srcX: p.x + p.w,
            srcY: ca.y,
            dstTbl: tName,
            dstCol: ca.target.split('.')[1] || ''
          });
        }
      });
    });

    const relSvg = drawRelations(positions, anchors, modCode);

    const svg = `<svg viewBox="0 0 ${W} ${totalH}" width="100%" style="max-width:${W}px;display:block;margin:0 auto" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <style>
          .tbl-box { cursor: pointer; }
          .tbl-box:hover .tbl-bg { fill: #1a2535 !important; stroke-width: 2 !important; }
          .rel-line { transition: opacity .15s, stroke-width .15s; }
          .ext-chip { transition: opacity .15s; pointer-events: none; }
          /* Highlight effect via :has() (modern browsers) */
          svg:has(.tbl-box:hover) .rel-line { opacity: 0.05 !important; }
          svg:has(.tbl-box:hover) .ext-chip { opacity: 0.2 !important; }
          svg:has(.tbl-box[data-sid="HOVER"]:hover) .rel-line[data-src="HOVER"] { opacity: 0.95 !important; stroke-width: 2.2 !important; }
        </style>
      </defs>
      ${tablesSvg}
      ${relSvg}
    </svg>`;
    container.innerHTML = svg;

    // JS-driven hover (fallback for browsers without :has())
    const svgEl = container.querySelector('svg');
    const allLines = svgEl.querySelectorAll('.rel-line');
    const allChips = svgEl.querySelectorAll('.ext-chip');
    svgEl.querySelectorAll('.tbl-box').forEach(box => {
      const sid = box.dataset.sid;
      box.addEventListener('mouseenter', () => {
        allLines.forEach(l => {
          if (l.dataset.src === sid) {
            l.setAttribute('opacity', '0.95');
            l.setAttribute('stroke-width', '2.4');
          } else {
            l.setAttribute('opacity', '0.04');
          }
        });
        allChips.forEach(c => {
          c.setAttribute('opacity', c.dataset.src === sid ? '1' : '0.15');
        });
      });
      box.addEventListener('mouseleave', () => {
        allLines.forEach(l => {
          l.setAttribute('opacity', '0.22');
          l.setAttribute('stroke-width', '1.4');
        });
        allChips.forEach(c => c.setAttribute('opacity', '1'));
      });
    });
  }

  document.addEventListener('DOMContentLoaded', () => {
    Object.keys(ERD_DATA).forEach(mod => {
      const el = document.getElementById(`erd-${mod.toLowerCase()}`);
      if (el) renderModule(mod, el);
    });
  });

  window.renderModule = renderModule;
})();
