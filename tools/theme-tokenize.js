// 라이트(Day) 테마 색 토큰화 — 다크 계산값은 그대로 두고, 라이트에서만 읽히는 색으로 바꿀 수 있게 고정 색을 CSS 변수로 치환한다.
//   node tools/theme-tokenize.js [--write]      (인자 없으면 모의 실행: 바뀔 건수만 출력)
// 대상: wwwroot/app.css(라이트 규칙 제외) · Components/**/*.razor.css · *.razor 의 style="…" 리터럴
// 규칙:
//   글자색(color)  Tailwind 50~600 계열·흰색 계열 → var(--ames-tx-*)  (라이트: 같은 색상의 700/800, 흰색은 --ames-text)
//   어두운 단색 배경(명도<0.1, 불투명도≥0.6) → var(--ames-sf-*)       (라이트: --ames-surface)
//   흰색 반투명 테두리 rgba(255,255,255,a) → var(--ames-bd-*)          (라이트: 아이보리 톤 테두리)
//   같은 규칙/스타일에 진한 색 배경이 있으면 글자색을 건드리지 않는다(색 버튼 위 흰 글자 등).
//   제외: @media print, 인쇄 전용 파일, 사출 현황판(InjShots — 대형 화면 다크 디자인), 로그인 화면, 배경막(backdrop/mask/overlay)
// 토큰 정의는 app.css 의 "THEME TOKENS" 두 블록(:root = 다크 원값, html[data-theme=light] = 라이트 값)에 다시 쓴다(멱등).
const fs = require('fs'), path = require('path');
const WEB = path.join(__dirname, '..', 'src', '06_Web', 'AMES.Web');
const WRITE = process.argv.includes('--write');
const SKIP_FILES = [/Display[\\/]InjShots\.razor\.css$/, /Portal[\\/]DeliveryNote\.razor\.css$/, /BoxLabelSheet\.razor\.css$/, /Display[\\/]InjShots\.razor$/];
const SKIP_SEL = /portal-auth|ames-auth|backdrop|overlay|-mask\b|\bprint\b|html:not\(\[data-theme/;

const TW = {
  slate: '#f8fafc #f1f5f9 #e2e8f0 #cbd5e1 #94a3b8 #64748b #475569 #334155 #1e293b #0f172a',
  gray: '#f9fafb #f3f4f6 #e5e7eb #d1d5db #9ca3af #6b7280 #4b5563 #374151 #1f2937 #111827',
  red: '#fef2f2 #fee2e2 #fecaca #fca5a5 #f87171 #ef4444 #dc2626 #b91c1c #991b1b #7f1d1d',
  orange: '#fff7ed #ffedd5 #fed7aa #fdba74 #fb923c #f97316 #ea580c #c2410c #9a3412 #7c2d12',
  amber: '#fffbeb #fef3c7 #fde68a #fcd34d #fbbf24 #f59e0b #d97706 #b45309 #92400e #78350f',
  yellow: '#fefce8 #fef9c3 #fef08a #fde047 #facc15 #eab308 #ca8a04 #a16207 #854d0e #713f12',
  lime: '#f7fee7 #ecfccb #d9f99d #bef264 #a3e635 #84cc16 #65a30d #4d7c0f #3f6212 #365314',
  green: '#f0fdf4 #dcfce7 #bbf7d0 #86efac #4ade80 #22c55e #16a34a #15803d #166534 #14532d',
  emerald: '#ecfdf5 #d1fae5 #a7f3d0 #6ee7b7 #34d399 #10b981 #059669 #047857 #065f46 #064e3b',
  teal: '#f0fdfa #ccfbf1 #99f6e4 #5eead4 #2dd4bf #14b8a6 #0d9488 #0f766e #115e59 #134e4a',
  cyan: '#ecfeff #cffafe #a5f3fc #67e8f9 #22d3ee #06b6d4 #0891b2 #0e7490 #155e75 #164e63',
  sky: '#f0f9ff #e0f2fe #bae6fd #7dd3fc #38bdf8 #0ea5e9 #0284c7 #0369a1 #075985 #0c4a6e',
  blue: '#eff6ff #dbeafe #bfdbfe #93c5fd #60a5fa #3b82f6 #2563eb #1d4ed8 #1e40af #1e3a8a',
  indigo: '#eef2ff #e0e7ff #c7d2fe #a5b4fc #818cf8 #6366f1 #4f46e5 #4338ca #3730a3 #312e81',
  violet: '#f5f3ff #ede9fe #ddd6fe #c4b5fd #a78bfa #8b5cf6 #7c3aed #6d28d9 #5b21b6 #4c1d95',
  purple: '#faf5ff #f3e8ff #e9d5ff #d8b4fe #c084fc #a855f7 #9333ea #7e22ce #6b21a8 #581c87',
  fuchsia: '#fdf4ff #fae8ff #f5d0fe #f0abfc #e879f9 #d946ef #c026d3 #a21caf #86198f #701a75',
  pink: '#fdf2f8 #fce7f3 #fbcfe8 #f9a8d4 #f472b6 #ec4899 #db2777 #be185d #9d174d #831843',
  rose: '#fff1f2 #ffe4e6 #fecdd3 #fda4af #fb7185 #f43f5e #e11d48 #be123c #9f1239 #881337',
};
const SHADES = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900];
const twIndex = {};
for (const [hue, list] of Object.entries(TW)) list.split(' ').forEach((hex, i) => { twIndex[hex] = { hue, shade: SHADES[i], i }; });
const twHex = (hue, shade) => TW[hue].split(' ')[SHADES.indexOf(shade)];
// AMES 고유 밝은 글자색(다크 전용 톤) → 라이트 대응
const CUSTOM_TEXT = { '#e5edf7': 'var(--ames-text)', '#dbe5ef': 'var(--ames-text)', '#f0f6fc': 'var(--ames-text)', '#93a4bd': 'var(--ames-text-dim)', '#c4dafe': '#1e40af' };

const norm = s => s.toLowerCase().replace(/\s+/g, '');
function parse(c) {
  c = norm(c);
  if (c.startsWith('#')) { let h = c.slice(1); if (h.length === 3) h = [...h].map(x => x + x).join(''); if (!/^[0-9a-f]{6}([0-9a-f]{2})?$/.test(h)) return null; return { r: parseInt(h.slice(0, 2), 16), g: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16), a: h.length === 8 ? parseInt(h.slice(6), 16) / 255 : 1, hex: '#' + h.slice(0, 6) }; }
  const m = c.match(/^rgba?\(([^)]*)\)$/); if (!m) return null;
  const v = m[1].split(/[,/]/).map(Number); if (v.length < 3 || v.some(Number.isNaN)) return null;
  const hex = '#' + v.slice(0, 3).map(x => Math.round(x).toString(16).padStart(2, '0')).join('');
  return { r: v[0], g: v[1], b: v[2], a: v.length > 3 ? v[3] : 1, hex };
}
const lum = c => { const f = v => { v /= 255; return v <= .03928 ? v / 12.92 : Math.pow((v + .055) / 1.055, 2.4); }; return .2126 * f(c.r) + .7152 * f(c.g) + .0722 * f(c.b); };
const aKey = a => 'a' + String(Math.round(a * 100)).padStart(2, '0');
const tokens = new Map();   // name -> { dark, light }
const token = (name, dark, light) => { if (!tokens.has(name)) tokens.set(name, { dark, light }); return `var(${name})`; };

// 글자색 리터럴 → 토큰(없으면 null)
function textToken(lit) {
  const c = parse(lit); if (!c) return null;
  const isWhite = c.r >= 240 && c.g >= 240 && c.b >= 240;
  if (c.a < 1) {
    if (isWhite) return token(`--ames-tx-white-${aKey(c.a)}`, lit, c.a >= .7 ? 'var(--ames-text)' : 'var(--ames-text-dim)');
    const t = twIndex[c.hex]; if (!t || t.shade >= 600) return null;
    return token(`--ames-tx-${t.hue}-${t.shade}-${aKey(c.a)}`, lit, c.a < .5 ? withAlpha(lightText(t), Math.min(1, c.a + .2)) : lightText(t));
  }
  if (isWhite && !twIndex[c.hex]) return token('--ames-tx-white', lit, 'var(--ames-text)');
  if (CUSTOM_TEXT[c.hex]) return token(`--ames-tx-${c.hex.slice(1)}`, lit, CUSTOM_TEXT[c.hex]);
  const t = twIndex[c.hex]; if (!t || t.shade >= 700) return null;
  if ((t.hue === 'slate' || t.hue === 'gray') && t.shade >= 600) return null;
  return token(`--ames-tx-${t.hue}-${t.shade}`, lit, t.shade === 50 && (t.hue === 'slate' || t.hue === 'gray') ? 'var(--ames-text)' : lightText(t));
}
function withAlpha(hex, a) { const c = parse(hex); return `rgba(${c.r},${c.g},${c.b},${+a.toFixed(2)})`; }
function lightText(t) {
  if (t.hue === 'slate' || t.hue === 'gray') return twHex(t.hue, t.shade <= 300 ? 700 : 600);
  return twHex(t.hue, t.shade <= 200 ? 800 : 700);
}
// 순수 검정 반투명은 배경막(모달 뒤)이라 제외
function isDarkSolid(lit) { const c = parse(lit); return c && c.a >= .6 && lum(c) < .1 && !(c.r === 0 && c.g === 0 && c.b === 0); }
function surfaceToken(lit) { const c = parse(lit); const key = c.a < 1 ? `${c.hex.slice(1)}-${aKey(c.a)}` : c.hex.slice(1); return token(`--ames-sf-${key}`, lit, 'var(--ames-surface)'); }
function borderToken(lit) { const c = parse(lit); if (!c || c.a >= 1 || !(c.r >= 240 && c.g >= 240 && c.b >= 240)) return null; return token(`--ames-bd-white-${aKey(c.a)}`, lit, `rgba(120,98,52,${Math.min(.35, +(c.a * 2.5 + .1).toFixed(2))})`); }

// 색 버튼·배지·칩 위 흰 글자는 배경이 다른 규칙에 있을 수 있어 건드리지 않는다
const WHITE_SKIP_SEL = /btn|button|badge|chip|pill|\btag\b|toast/i;
const isWhiteLit = l => { const c = parse(l); return c && c.r >= 240 && c.g >= 240 && c.b >= 240; };
const LIT = /#[0-9a-fA-F]{3,8}\b|rgba?\([^)]*\)/g;
// 이 선언 묶음(규칙 본문 또는 style 속성)에 진한 색 배경이 있나 — 있으면 글자색을 건드리지 않는다
function hasColoredBg(decls) {
  for (const d of decls) {
    if (!/^background(-color)?$/.test(d.prop)) continue;
    const v = d.value.replace(/!important/i, '').trim();
    if (/^(transparent|none|inherit|initial|unset)$/i.test(v)) continue;
    const lits = v.match(LIT) || [];
    if (!lits.length) return true;                               // var(--rz-primary) 등
    for (const l of lits) { const c = parse(l); if (!c) return true; if (c.a < .35) continue; if (isDarkSolid(l)) continue; return true; }
  }
  return false;
}
// 선언 목록 → 치환 목록 [{start,end,text}] (start/end 는 value 내부 상대 위치 + value 시작)
function transformDecls(decls, stats, sel = '') {
  const out = [];
  const coloredBg = hasColoredBg(decls);
  // 버튼류 선택자라도 같은 규칙이 배경을 투명·옅은 틴트로 명시하면(고스트 버튼 등) 흰 글자가 라이트에서 사라지므로 변환한다
  const clearBg = !coloredBg && decls.some(d => /^background(-color)?$/.test(d.prop));
  for (const d of decls) {
    const prop = d.prop;
    let kind = null;
    if (prop === 'color') kind = coloredBg ? null : 'text';
    else if (/^background(-color|-image)?$/.test(prop)) kind = 'bg';
    else if (/^border(-(top|bottom|left|right|block|inline))?(-color)?$/.test(prop) || prop === 'outline') kind = 'border';
    if (!kind) continue;
    for (const m of d.value.matchAll(LIT)) {
      const lit = m[0]; let rep = null;
      if (kind === 'text') rep = WHITE_SKIP_SEL.test(sel) && isWhiteLit(lit) && !clearBg ? null : textToken(lit);
      else if (kind === 'bg') rep = isDarkSolid(lit) ? surfaceToken(lit) : null;
      else rep = borderToken(lit);
      if (rep) { out.push({ start: d.vstart + m.index, end: d.vstart + m.index + lit.length, text: rep }); stats[kind]++; }
    }
  }
  return out;
}
// 선언 문자열 → [{prop, value, vstart}] (offset 기준)
function splitDecls(body, offset) {
  const res = []; const re = /(^|;)\s*([a-zA-Z-]+)\s*:\s*([^;]*)/g; let m;
  while ((m = re.exec(body))) { const vstart = offset + m.index + m[0].length - m[3].length; res.push({ prop: m[2].toLowerCase(), value: m[3], vstart }); }
  return res;
}
// CSS 텍스트 변환 (주석·중첩 @media 처리)
function transformCss(text, stats, isApp) {
  const edits = [];
  let i = 0; const stack = []; let preludeStart = 0;
  const lightStart = isApp ? text.indexOf('THEME TOKENS (light)') : -1;
  while (i < text.length) {
    if (text.startsWith('/*', i)) { const e = text.indexOf('*/', i + 2); i = e < 0 ? text.length : e + 2; if (!stack.length || stack[stack.length - 1].type !== 'rule') preludeStart = i; continue; }
    const ch = text[i];
    if (ch === '{') {
      const prelude = text.slice(preludeStart, i).replace(/\/\*[\s\S]*?\*\//g, '').trim();
      let type = 'rule';
      if (prelude.startsWith('@')) type = /^@(media|supports|container|layer)/.test(prelude) ? 'group' : 'skip';
      const parentSkip = stack.some(s => s.skip);
      const skip = parentSkip || type === 'skip' || (type === 'group' && /\bprint\b/.test(prelude)) || (type === 'rule' && (/data-theme/.test(prelude) || SKIP_SEL.test(prelude) || /^(from|to|\d+%)/.test(prelude) || prelude === ':root'));
      stack.push({ type, skip, bodyStart: i + 1, prelude });
      i++; preludeStart = i; continue;
    }
    if (ch === '}') {
      const top = stack.pop();
      if (top && top.type === 'rule' && !top.skip && !(lightStart >= 0 && top.bodyStart > lightStart && top.bodyStart < text.indexOf('THEME TOKENS END', lightStart))) {
        const body = text.slice(top.bodyStart, i);
        edits.push(...transformDecls(splitDecls(body, top.bodyStart), stats, top.prelude));
      }
      i++; preludeStart = i; continue;
    }
    if (ch === ';' && (!stack.length || stack[stack.length - 1].type !== 'rule')) preludeStart = i + 1;
    i++;
  }
  return apply(text, edits);
}
function apply(text, edits) {
  edits.sort((a, b) => b.start - a.start);
  for (const e of edits) text = text.slice(0, e.start) + e.text + text.slice(e.end);
  return text;
}
function transformRazor(text, stats) {
  // 1) style="…" 의 리터럴 선언 (Razor 식이 든 값은 건너뜀)
  const edits = [];
  for (const m of text.matchAll(/style="([^"]*)"/g)) {
    const vs = m.index + m[0].indexOf('"') + 1;
    edits.push(...transformDecls(splitDecls(m[1], vs).filter(d => !d.value.includes('@')), stats));
  }
  text = apply(text, edits);
  // 2) C# 식 안의 글자색: "color:#f87171;" 문자열 · color:@(a ? "#x" : "#y") 삼항 — 흰색은 색 배경 위일 수 있어 제외(textToken 이 흰색 외만 처리하도록 거른다)
  const cs = [];
  const conv = lit => { const c = parse(lit); if (!c || (c.r >= 240 && c.g >= 240 && c.b >= 240)) return null; return textToken(lit); };
  for (const m of text.matchAll(/(?<![-\w])color:\s*(#[0-9a-fA-F]{3,8})\b/g)) {
    const rep = conv(m[1]); if (!rep) continue;
    const s = m.index + m[0].length - m[1].length; cs.push({ start: s, end: s + m[1].length, text: rep }); stats.text++;
  }
  for (const m of text.matchAll(/(?<![-\w])color:\s*@\(/g)) {
    let depth = 1, j = m.index + m[0].length;
    while (j < text.length && depth) { if (text[j] === '(') depth++; else if (text[j] === ')') depth--; j++; }
    const expr = text.slice(m.index + m[0].length, j - 1);
    for (const q of expr.matchAll(/"(#[0-9a-fA-F]{3,8})"/g)) {
      const rep = conv(q[1]); if (!rep) continue;
      const s = m.index + m[0].length + q.index + 1; cs.push({ start: s, end: s + q[1].length, text: rep }); stats.text++;
    }
  }
  return apply(text, cs);
}

// ── 실행 ──
const files = [];
(function w(d) { for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); if (e.isDirectory()) { if (['bin', 'obj', 'lib', 'node_modules'].includes(e.name)) continue; w(p); } else files.push(p); } })(path.join(WEB, 'Components'));
const report = [];
const appPath = path.join(WEB, 'wwwroot', 'app.css');
const targets = [appPath, ...files.filter(f => /\.razor(\.css)?$/.test(f))];
const results = [];
for (const f of targets) {
  if (SKIP_FILES.some(r => r.test(f))) continue;
  const orig = fs.readFileSync(f, 'utf8');
  const stats = { text: 0, bg: 0, border: 0 };
  const isApp = f === appPath;
  const next = f.endsWith('.razor') ? transformRazor(orig, stats) : transformCss(orig, stats, isApp);
  if (next !== orig) results.push({ f, orig, next, stats });
}
// 토큰 정의 블록 갱신(app.css)
const app = results.find(r => r.f === appPath) || { f: appPath, orig: fs.readFileSync(appPath, 'utf8'), stats: { text: 0, bg: 0, border: 0 } };
app.next ??= app.orig;
// 기존 블록의 토큰도 유지(멱등: 두 번째 실행에서 이미 치환된 파일은 토큰을 다시 만들지 않으므로)
const darkDefs = /THEME TOKENS \(dark\)[\s\S]*?THEME TOKENS END/.exec(app.next)?.[0] ?? '';
for (const m of darkDefs.matchAll(/(--ames-(?:tx|sf|bd)-[a-z0-9-]+): ([^;]+);/g)) if (!tokens.has(m[1])) tokens.set(m[1], { dark: m[2], light: null });
for (const m of app.next.matchAll(/THEME TOKENS \(light\)[\s\S]*?THEME TOKENS END/g)) for (const t of m[0].matchAll(/(--ames-(?:tx|sf|bd)-[a-z0-9-]+): ([^;]+);/g)) if (tokens.has(t[1]) && !tokens.get(t[1]).light) tokens.get(t[1]).light = t[2];
const names = [...tokens.keys()].sort();
let a = app.next;
const EOL = a.includes('\r\n') ? '\r\n' : '\n';   // 작업 트리는 autocrlf 로 CRLF 일 수 있다
const lines = arr => arr.join(EOL);
const darkBlock = lines(['/* ═══ THEME TOKENS (dark) — tools/theme-tokenize.js 가 만든다. 값은 다크 원래 색, 라이트 값은 아래 (light) 블록 ═══ */', ':root {', ...names.map(n => `    ${n}: ${tokens.get(n).dark};`), '}', '/* ═══ THEME TOKENS END ═══ */']);
const lightBlock = lines(['/* ═══ THEME TOKENS (light) — 라이트에서 읽히는 색. 글자 = 같은 색상 700/800, 어두운 배경 = --ames-surface ═══ */', 'html[data-theme="light"] {', ...names.map(n => `    ${n}: ${tokens.get(n).light};`), '}', '/* ═══ THEME TOKENS END ═══ */']);
const replaceBlock = (txt, tag, block, anchorAfter) => {
  const re = new RegExp(`/\\* ═══ THEME TOKENS \\(${tag}\\)[\\s\\S]*?/\\* ═══ THEME TOKENS END ═══ \\*/`);
  if (re.test(txt)) return txt.replace(re, block);
  const k = txt.indexOf(anchorAfter); if (k < 0) throw new Error('anchor not found: ' + anchorAfter);
  const mm = /\r?\n\}\r?\n/.exec(txt.slice(k)); if (!mm) throw new Error('block end not found: ' + anchorAfter);
  const end = k + mm.index + mm[0].length;
  return txt.slice(0, end) + EOL + block + EOL + txt.slice(end);
};
a = replaceBlock(a, 'dark', darkBlock, ':root {');
a = replaceBlock(a, 'light', lightBlock, 'html[data-theme="light"] {');
app.next = a;
if (!results.includes(app)) results.push(app);
let tot = { text: 0, bg: 0, border: 0 };
for (const r of results) { for (const k in tot) tot[k] += r.stats[k]; report.push(`${String(r.stats.text).padStart(4)} ${String(r.stats.bg).padStart(4)} ${String(r.stats.border).padStart(4)}  ${path.relative(WEB, r.f).split(path.sep).join('/')}`); }
console.log(`파일 ${results.length} · 글자 ${tot.text} · 배경 ${tot.bg} · 테두리 ${tot.border} · 토큰 ${names.length}`);
console.log(report.join('\n'));
if (WRITE) for (const r of results) fs.writeFileSync(r.f, r.next);
else fs.writeFileSync(path.join(require('os').tmpdir(), 'theme-tokens-preview.txt'), names.map(n => `${n}\t${tokens.get(n).dark}\t${tokens.get(n).light}`).join('\n'));
