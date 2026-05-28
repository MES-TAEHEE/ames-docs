// ════════════════════════════════════════════════════════════════════════
//  A-MES ERD · Module-detail renderer
//  Reads ERD_DATA (from AMES_ERD_data.js) and renders per-module SVG ERDs
//  Each module: tables in 4-col flow layout + internal FK curves
//                + external FK chips for cross-module references
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

  // Detect module code from table name prefix
  function modOf(tblName) {
    if (tblName.startsWith('AspNet')) return 'ID';
    if (tblName.startsWith('tbl_'))   return 'PR';   // tbl_Lot anchor
    if (tblName.startsWith('PNT_'))   return 'PNT';
    if (tblName.startsWith('PR_'))    return 'PR';
    const prefix = tblName.split('_')[0];
    return MOD_COLOR[prefix] ? prefix : '?';
  }

  // Render a single table box (returns SVG string + records col Y positions for FK anchoring)
  function renderTableBox(tbl, x, y, w, color, anchors) {
    const HDR_H = 30;
    const ROW_H = 14;
    const h = HDR_H + tbl.cols.length * ROW_H + 6;
    let s = '';
    s += `<g class="tbl-box" data-tbl="${tbl.name}" style="--mc:${color}">`;
    s += `<rect class="tbl-bg" x="${x}" y="${y}" width="${w}" height="${h}" rx="4"/>`;
    s += `<rect class="tbl-hdr" x="${x}" y="${y}" width="${w}" height="${HDR_H}" rx="4"/>`;
    s += `<rect class="tbl-hdr" x="${x}" y="${y + HDR_H - 6}" width="${w}" height="6"/>`;
    s += `<text class="t-name" x="${x + 9}" y="${y + 14}">${tbl.name}</text>`;
    if (tbl.ko) s += `<text class="t-tag" x="${x + 9}" y="${y + 25}">${escapeXml(tbl.ko)}</text>`;
    tbl.cols.forEach((c, i) => {
      const [name, type, flag] = c;
      const ry = y + HDR_H + 4 + i * ROW_H;
      const icon = flag === 'PK' ? '◆' : (flag.startsWith('FK') ? '◇' : '');
      const cls = flag === 'PK' ? 'pk' : (flag.startsWith('FK') ? 'fk' : '');
      s += `<text class="t-col ${cls}" x="${x + 8}" y="${ry + 9}">${icon}</text>`;
      s += `<text class="t-col ${cls}" x="${x + 22}" y="${ry + 9}">${escapeXml(name)}</text>`;
      s += `<text class="t-col" x="${x + w - 8}" y="${ry + 9}" text-anchor="end" fill="#5a7088">${escapeXml(type)}</text>`;
      // Record anchor for FK line drawing
      if (flag.startsWith('FK')) {
        const target = flag.substring(3); // "MD_Item.ItemNo"
        const [tName, tCol] = target.split('.');
        anchors.push({ srcTbl: tbl.name, srcX: x + w, srcY: ry + 7, dstTbl: tName, dstCol: tCol, dstX: x, dstY: ry + 7 });
      }
    });
    s += `</g>`;
    return { svg: s, height: h };
  }

  function escapeXml(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  // Layout tables in flow (4 cols, shortest-column-first)
  function layoutTables(tables, totalW, cols) {
    const COL_GAP = 18;
    const ROW_GAP = 18;
    const colW = Math.floor((totalW - 40 - COL_GAP * (cols - 1)) / cols);
    const colTops = new Array(cols).fill(20);
    const positions = [];
    tables.forEach(t => {
      let ci = 0;
      for (let i = 1; i < cols; i++) if (colTops[i] < colTops[ci]) ci = i;
      const x = 20 + ci * (colW + COL_GAP);
      const y = colTops[ci];
      const h = 30 + t.cols.length * 14 + 6;
      positions.push({ t, x, y, w: colW, h });
      colTops[ci] = y + h + ROW_GAP;
    });
    return { positions, totalH: Math.max(...colTops) + 20, colW };
  }

  // Draw FK lines for internal references; collect external refs separately
  function drawRelations(positions, modCode, anchors) {
    const tblIndex = {};
    positions.forEach(p => { tblIndex[p.t.name] = p; });
    let lines = '';
    const externals = []; // {srcTbl, dstTbl, srcX, srcY, dstMod}
    anchors.forEach(a => {
      const dst = tblIndex[a.dstTbl];
      if (dst) {
        // Internal FK: source right side → target left side
        const sx = a.srcX, sy = a.srcY;
        const tx = dst.x, ty = dst.y + 18; // aim at top of target box
        // Bezier control points
        const dx = Math.abs(tx - sx);
        const c1x = sx + Math.min(60, dx / 2);
        const c2x = tx - Math.min(60, dx / 2);
        lines += `<path class="rel-line solid" d="M ${sx} ${sy} C ${c1x} ${sy}, ${c2x} ${ty}, ${tx} ${ty}" stroke="${MOD_COLOR[modCode]}" opacity="0.55" marker-end="url(#mod-arr-${modCode})"/>`;
      } else {
        // External FK: target in another module
        const targetMod = modOf(a.dstTbl);
        externals.push({ srcX: a.srcX, srcY: a.srcY, dstTbl: a.dstTbl, dstMod: targetMod });
      }
    });
    // External chip at row right edge
    externals.forEach(e => {
      const chipX = e.srcX + 4;
      const chipY = e.srcY - 7;
      const text = `→ ${e.dstTbl}`;
      const w = text.length * 5.5 + 14;
      lines += `<g class="ext-chip">`;
      lines += `<rect x="${chipX}" y="${chipY}" width="${w}" height="13" rx="2" fill="#1a2535" stroke="${MOD_COLOR[e.dstMod] || '#3a5070'}" stroke-width="1" stroke-dasharray="2,2"/>`;
      lines += `<text x="${chipX + 4}" y="${chipY + 9}" font-size="8" fill="${MOD_COLOR[e.dstMod] || '#94a3b8'}" font-family="JetBrains Mono,monospace" font-weight="700">${escapeXml(text)}</text>`;
      lines += `</g>`;
    });
    return lines;
  }

  function renderModule(modCode, container) {
    const mod = ERD_DATA[modCode];
    if (!mod || !mod.tables || mod.tables.length === 0) {
      container.innerHTML = `<div style="color:#5a7088;padding:40px;text-align:center;font-style:italic">No tables defined for module ${modCode} yet.</div>`;
      return;
    }
    const color = MOD_COLOR[modCode] || '#94a3b8';
    const W = 1480;
    const COLS = mod.cols || 4;

    const { positions, totalH, colW } = layoutTables(mod.tables, W, COLS);
    const anchors = [];

    let svg = `<svg viewBox="0 0 ${W} ${totalH}" width="100%" style="max-width:${W}px" xmlns="http://www.w3.org/2000/svg">`;
    svg += `<defs>`;
    for (const m in MOD_COLOR) {
      svg += `<marker id="mod-arr-${m}" markerWidth="7" markerHeight="7" refX="6" refY="3.5" orient="auto"><path d="M0,0 L7,3.5 L0,7 Z" fill="${MOD_COLOR[m]}"/></marker>`;
    }
    svg += `</defs>`;

    // Draw boxes
    positions.forEach(p => {
      const { svg: boxSvg } = renderTableBox(p.t, p.x, p.y, p.w, color, anchors);
      svg += boxSvg;
    });

    // Adjust anchor source X (right edge of box) and target X (left edge of box)
    // Already set during renderTableBox; just need to update src to be box right edge precisely
    anchors.forEach(a => {
      // Find the source box position
      const src = positions.find(p => p.t.name === a.srcTbl);
      if (src) { a.srcX = src.x + src.w; }
    });

    // Draw relation lines (internal) and external chips
    svg += drawRelations(positions, modCode, anchors);

    svg += `</svg>`;
    container.innerHTML = svg;
  }

  // Render all modules on load
  document.addEventListener('DOMContentLoaded', () => {
    Object.keys(ERD_DATA).forEach(mod => {
      const el = document.getElementById(`erd-${mod.toLowerCase()}`);
      if (el) renderModule(mod, el);
    });
  });

  // Expose for debug
  window.renderModule = renderModule;
})();
