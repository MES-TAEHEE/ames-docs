# docs/menu-guide/*.md 를 한 파일 HTML(왼쪽 목차·검색, 메뉴별 이동)로 묶는다.
# 실행: python tools/menu_guide_html.py  →  docs/menu-guide/AMES_메뉴_동작기준.html
# md 를 고친 뒤 다시 실행하면 된다. 외부 패키지 없이 이 문서들에 쓰인 마크다운(제목·목록·표·코드 블록·인용·굵게·코드·링크)만 변환한다.
import html, re, pathlib, datetime

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "menu-guide"
OUT = SRC / "AMES_메뉴_동작기준.html"
FILES = ["README.md"] + sorted(p.name for p in SRC.glob("0*.md"))
CODE_RE = re.compile(r"^([A-Z]+-[A-Z0-9]+(?:\s*/\s*[A-Z]+-[A-Z0-9]+)*)")

used_ids = set()
def make_id(text, prefix):
    m = CODE_RE.match(text)
    base = m.group(1).split("/")[0].strip() if m else prefix + "-" + re.sub(r"[^0-9A-Za-z가-힣]+", "-", text).strip("-")[:40]
    i, cand = 1, base
    while cand in used_ids:
        i += 1; cand = f"{base}-{i}"
    used_ids.add(cand)
    return cand

def file_anchor(name):
    return "sec-" + name.split("_")[0].replace(".md", "").lower()

def inline(s):
    out, parts = [], re.split(r"(`[^`]+`)", s)
    for p in parts:
        if p.startswith("`") and p.endswith("`") and len(p) > 1:
            out.append("<code>" + html.escape(p[1:-1]) + "</code>"); continue
        t = html.escape(p, quote=False)
        t = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", t)
        def link(m):
            label, href = m.group(1), m.group(2)
            if href.endswith(".md"): href = "#" + file_anchor(href)
            return f'<a href="{html.escape(href)}">{label}</a>'
        t = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", link, t)
        out.append(t)
    return "".join(out)

def table(raw_rows):
    rows = [[c.strip() for c in r.strip().strip("|").split("|")] for r in raw_rows]
    head, body = rows[0], [r for r in rows[1:] if not all(re.fullmatch(r":?-+:?", c) for c in r)]
    t = ["<div class='tbl'><table><thead><tr>" + "".join(f"<th>{inline(c)}</th>" for c in head) + "</tr></thead><tbody>"]
    t += ["<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>" for r in body]
    return "".join(t) + "</tbody></table></div>"

def convert(md, fname, nav):
    lines = md.splitlines()
    out, i, n = [], 0, len(lines)
    para = []
    def flush():
        if para:
            out.append("<p>" + inline(" ".join(para)) + "</p>"); para.clear()
    while i < n:
        line = lines[i]
        if line.startswith("```"):
            flush(); i += 1; buf = []
            while i < n and not lines[i].startswith("```"):
                buf.append(lines[i]); i += 1
            out.append("<pre>" + html.escape("\n".join(buf)) + "</pre>"); i += 1; continue
        m = re.match(r"^(#{1,4})\s+(.*)", line)
        if m:
            flush(); lvl, text = len(m.group(1)), m.group(2).strip()
            if lvl == 1:
                aid = file_anchor(fname)
                nav.append((1, aid, text))
                out.append(f'<h1 id="{aid}">{inline(text)}</h1>')
            else:
                aid = make_id(text, file_anchor(fname))
                if lvl <= 3: nav.append((lvl, aid, text))
                out.append(f'<h{lvl} id="{aid}">{inline(text)}<a class="top" href="#top" title="맨 위로">↑</a></h{lvl}>')
            i += 1; continue
        if line.startswith("|"):
            flush(); rows = []
            while i < n and lines[i].startswith("|"):
                rows.append(lines[i]); i += 1
            out.append(table(rows)); continue
        if line.startswith(">"):
            flush(); buf = []
            while i < n and lines[i].startswith(">"):
                buf.append(lines[i].lstrip(">").strip()); i += 1
            out.append("<blockquote>" + inline(" ".join(buf)) + "</blockquote>"); continue
        if re.match(r"^\s*(-|\d+\.)\s+", line):
            flush(); stack = []   # (indent, tag)
            while i < n and (re.match(r"^\s*(-|\d+\.)\s+", lines[i]) or (lines[i].startswith("  ") and lines[i].strip())):
                if lines[i].lstrip().startswith("|"):   # 목록 항목 안의 표 — 앞 항목 안에 넣는다
                    rows = []
                    while i < n and lines[i].lstrip().startswith("|"):
                        rows.append(lines[i]); i += 1
                    out[-1] = out[-1][:-5] + table(rows) + "</li>"; continue
                lm = re.match(r"^(\s*)(-|\d+\.)\s+(.*)", lines[i])
                if not lm:   # 이어지는 줄은 앞 항목 글에 붙인다
                    out[-1] = out[-1][:-5] + " " + inline(lines[i].strip()) + "</li>"; i += 1; continue
                ind, tag = len(lm.group(1)), ("ol" if lm.group(2)[0].isdigit() else "ul")
                while stack and stack[-1][0] > ind:
                    out.append(f"</{stack.pop()[1]}>")
                if not stack or stack[-1][0] < ind:
                    stack.append((ind, tag)); out.append(f"<{tag}>")
                out.append("<li>" + inline(lm.group(3)) + "</li>"); i += 1
            while stack: out.append(f"</{stack.pop()[1]}>")
            continue
        if line.strip() == "---":
            flush(); out.append("<hr>"); i += 1; continue
        if not line.strip():
            flush(); i += 1; continue
        para.append(line.strip()); i += 1
    flush()
    return "\n".join(out)

sections, nav = [], []
for f in FILES:
    sections.append(f'<section class="doc">{convert((SRC / f).read_text(encoding="utf-8"), f, nav)}</section>')

nav_html = []
for lvl, aid, text in nav:
    m = CODE_RE.match(text)
    label = html.escape(text)
    if m and lvl > 1:
        code = m.group(1); rest = text[len(code):].strip()
        label = f'<span class="cd">{html.escape(code)}</span>{html.escape(rest)}'
    nav_html.append(f'<a class="l{lvl}" href="#{aid}">{label}</a>')

stamp = datetime.date.today().isoformat()
page = f"""<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>AMES 메뉴 동작 기준</title>
<style>
:root{{--bg:#ffffff;--fg:#1f2328;--dim:#59636e;--side:#f6f8fa;--bd:#d1d9e0;--acc:#0969da;--code:#eff2f5;--hi:#ddf4ff;}}
@media (prefers-color-scheme: dark){{:root{{--bg:#0d1117;--fg:#e6edf3;--dim:#9198a1;--side:#151b23;--bd:#30363d;--acc:#4493f8;--code:#1f2630;--hi:#12304f;}}}}
*{{box-sizing:border-box}} html{{scroll-padding-top:12px}}
body{{margin:0;background:var(--bg);color:var(--fg);font:15px/1.65 "Segoe UI","Malgun Gothic",sans-serif}}
nav{{position:fixed;top:0;left:0;bottom:0;width:300px;background:var(--side);border-right:1px solid var(--bd);display:flex;flex-direction:column}}
nav .hd{{padding:14px 14px 8px;font-weight:700}} nav .hd small{{display:block;font-weight:400;color:var(--dim)}}
nav input{{margin:0 14px 8px;padding:7px 10px;border:1px solid var(--bd);border-radius:6px;background:var(--bg);color:var(--fg)}}
nav .list{{overflow:auto;padding:0 6px 20px}}
nav a{{display:block;color:var(--fg);text-decoration:none;padding:3px 8px;border-radius:5px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}}
nav a:hover{{background:var(--hi)}} nav a.on{{background:var(--hi);color:var(--acc)}}
nav a.l1{{font-weight:700;margin-top:10px}} nav a.l2{{padding-left:18px;font-size:14px}} nav a.l3{{padding-left:32px;font-size:13.5px;color:var(--dim)}}
nav .cd{{font-family:Consolas,monospace;font-size:12px;color:var(--acc);margin-right:6px}}
main{{margin-left:300px;padding:10px 40px 80px;max-width:1180px}}
.doc{{border-bottom:2px solid var(--bd);padding-bottom:24px;margin-bottom:12px}}
h1{{font-size:26px;margin:28px 0 12px;padding-bottom:6px;border-bottom:1px solid var(--bd)}}
h2{{font-size:20px;margin:30px 0 10px}} h3{{font-size:17px;margin:24px 0 8px}}
h2 .top,h3 .top{{margin-left:8px;font-size:13px;color:var(--dim);text-decoration:none;visibility:hidden}} h2:hover .top,h3:hover .top{{visibility:visible}}
code{{font-family:Consolas,monospace;font-size:13px;background:var(--code);padding:1px 5px;border-radius:4px}}
pre{{background:var(--code);padding:12px 14px;border-radius:6px;overflow:auto;font:13px/1.5 Consolas,"Malgun Gothic",monospace}}
.tbl{{overflow-x:auto}} table{{border-collapse:collapse;margin:8px 0}} th,td{{border:1px solid var(--bd);padding:5px 10px;vertical-align:top}} th{{background:var(--side)}}
blockquote{{margin:10px 0;padding:6px 14px;border-left:4px solid var(--bd);color:var(--dim)}}
a{{color:var(--acc)}} ul,ol{{padding-left:24px}} li{{margin:2px 0}} hr{{border:0;border-top:1px solid var(--bd);margin:22px 0}}
#menuBtn{{display:none}}
@media (max-width:900px){{nav{{transform:translateX(-100%);transition:transform .2s;z-index:10;width:86vw}} nav.open{{transform:none}}
 main{{margin-left:0;padding:10px 16px 60px}} #menuBtn{{display:block;position:fixed;right:14px;bottom:14px;z-index:11;padding:10px 14px;border-radius:20px;border:1px solid var(--bd);background:var(--side);color:var(--fg)}}}}
</style></head>
<body id="top">
<nav id="nav"><div class="hd">AMES 메뉴 동작 기준<small>생성 {stamp} · docs/menu-guide</small></div>
<input id="q" type="search" placeholder="메뉴 코드·이름 검색 (예: PP-OTD, 납기)">
<div class="list" id="list">{''.join(nav_html)}</div></nav>
<button id="menuBtn" onclick="document.getElementById('nav').classList.toggle('open')">목차</button>
<main>{''.join(sections)}</main>
<script>
const q=document.getElementById('q'),links=[...document.querySelectorAll('#list a')];
q.addEventListener('input',()=>{{const v=q.value.trim().toLowerCase();links.forEach(a=>a.style.display=!v||a.classList.contains('l1')||a.textContent.toLowerCase().includes(v)?'':'none');}});
links.forEach(a=>a.addEventListener('click',()=>document.getElementById('nav').classList.remove('open')));
const heads=links.map(a=>document.getElementById(a.getAttribute('href').slice(1))).filter(Boolean);
let cur=null;
const io=new IntersectionObserver(es=>{{es.forEach(e=>{{if(e.isIntersecting){{const a=links.find(l=>l.getAttribute('href')==='#'+e.target.id);if(a&&a!==cur){{cur&&cur.classList.remove('on');a.classList.add('on');cur=a;a.scrollIntoView({{block:'nearest'}});}}}}}});}},{{rootMargin:'0px 0px -75% 0px'}});
heads.forEach(h=>io.observe(h));
</script>
</body></html>"""
OUT.write_text(page, encoding="utf-8")
print(f"{OUT.name}: {len(nav)} nav entries".encode("ascii", "replace").decode())
