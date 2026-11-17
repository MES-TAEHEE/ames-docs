// AMES.Web 의 작업지시 팝업 「흐름」 보기 — React Flow(https://reactflow.dev, MIT)로 수주 → 계획 → WO → 단계 → 슬롯 → 실적을 그린다.
// Blazor 가 JS interop 으로 render(elementId, graph) → update(elementId, graph)(실시간 갱신) → dispose(elementId) 를 부른다.
//  · 사용자가 옮긴 노드 위치와 화면 위치·배율은 update 가 건드리지 않는다(데이터만 바꾼다).
//  · 진행 중(status 'cur') 노드는 펄스 테두리·「● 진행 중」 배지로 부각하고, 「진행 중 보기」 버튼이 그 노드로 화면을 맞춘다.
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import {
  ReactFlow, ReactFlowProvider, Background, Controls, MiniMap, Handle, Panel, Position, MarkerType,
  useNodesState, useEdgesState, useReactFlow,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';

// ───── 쉽게 바꾸는 값 ─────────────────────────────────────────────────────────────
// 바꾼 뒤: cd tools/flow-bundle && npm run build  → Web 재시작 · Ctrl+F5
const FIT = {
  // padding: 0.15,          // 화면 맞춤 여백(클수록 멀리서 보임) — 처음 열 때·「전체 보기」·크기 변경·전체화면 공통
  padding: '60px',          // 화면 맞춤 여백(클수록 멀리서 보임) — 처음 열 때·「전체 보기」·크기 변경·전체화면 공통
  durationMs: 300,        // 맞춤 애니메이션 시간
};
const FIT_RUNNING = { padding: 0.9, maxZoom: 0.9, fallbackPadding: 0.5 };   // 「● 진행 중 보기」 — maxZoom 이 확대 상한
const ZOOM = { min: 0.2, max: 1.8 };                                         // 사용자가 휠로 줄이고 늘릴 수 있는 한계
const GRID = { show: true, variant: 'dots', gap: 18, color: undefined, size: undefined };   // 배경 격자 — variant 'dots'|'lines'|'cross', color 예 '#334155'(없으면 기본색)
// const GRID = { show: true, variant: 'lines', gap: 18, color: '#1e293b', size: undefined };  // 격자를 선으로 바꾸고 색으로
const EDGE = {                                                               // 선(간선)
  width: undefined,       // 일반 선 굵기(예 2) — 없으면 기본(1)
  color: undefined,       // 일반 선 색(예 '#64748b') — 없으면 기본
  animatedWidth: 3,       // 진행 중으로 가는 점선 굵기
  animatedColor: '#3b82f6',
  arrowSize: undefined,   // 화살표 크기(예 20) — 없으면 기본
};
// const MINIMAP = { show: true };                                              // 오른쪽 아래 미니맵
const MINIMAP = { show: true, position: 'bottom-right', width: 100, height: 50, maskColor: 'rgba(0,0,0,0.5)' };
const INITIAL_FIT_DELAYS_MS = [200, 700];                                    // 팝업이 열리며 크기가 잡힌 뒤 처음 맞추는 시각(두 번)
// ───────────────────────────────────────────────────────────────────────────────

const KIND_COLOR = { so: '#38bdf8', plan: '#a78bfa', wo: '#34d399', step: '#fbbf24', slot: '#60a5fa', result: '#f472b6' };
const STATUS_COLOR = { done: '#22c55e', cur: '#3b82f6', todo: '#94a3b8', bad: '#ef4444' };
const fitOpts = (extra) => ({ padding: FIT.padding, duration: FIT.durationMs, ...extra });

function WoNode({ data }) {
  const [chipsOpen, setChipsOpen] = useState(false);
  const accent = data.status ? (STATUS_COLOR[data.status] ?? KIND_COLOR[data.kind]) : KIND_COLOR[data.kind];
  const cur = data.status === 'cur';
  return (
    <div className={'ames-flow-node' + (cur ? ' ames-flow-cur' : '') + (data.status === 'done' ? ' ames-flow-done' : '')}
         style={{ borderColor: accent, '--flow-accent': accent }}>
      <Handle type="target" position={Position.Left} style={{ background: accent }} />
      <Handle id="t" type="target" position={Position.Top} style={{ background: accent }} />
      <div className="ames-flow-head">
        <span className="ames-flow-kind" style={{ color: accent }}>{data.kindLabel}</span>
        {data.statusLabel ? <span className={'ames-flow-badge ames-flow-badge-' + data.status}>{cur ? '● ' : data.status === 'done' ? '✓ ' : ''}{data.statusLabel}</span> : null}
      </div>
      <div className="ames-flow-title">{data.title}</div>
      {(data.lines || []).map((l, i) => <div key={i} className="ames-flow-line">{l}</div>)}
      {data.operatorRows && data.operatorRows.length ? (
        <div className="ames-flow-ops">
          {data.operatorRows.map((o, i) => (
            <div key={i} className={'ames-flow-op' + (o.live ? ' ames-flow-op-live' : '')}>{o.live ? '● ' : '👤 '}{o.text}</div>
          ))}
        </div>
      ) : null}
      {data.chips && data.chips.length ? (
        <div className={'ames-flow-chips nodrag nopan nowheel' + (chipsOpen ? ' ames-flow-chips-open' : '')}>
          {(chipsOpen ? data.chips : data.chips.slice(0, CHIPS_SHOWN)).map(c => (
            <button key={c.key} type="button" className="ames-flow-chip" title={c.key}
                    onClick={() => dotnetRefs.get(data.hostId)?.invokeMethodAsync('OpenLot', c.key)}>{c.text}</button>
          ))}
          {data.chips.length > CHIPS_SHOWN ? (
            <button type="button" className="ames-flow-more" onClick={() => setChipsOpen(v => !v)}>{chipsOpen ? '▲' : '+' + (data.chips.length - CHIPS_SHOWN)}</button>
          ) : null}
        </div>
      ) : null}
      {typeof data.progress === 'number' ? (
        <div className="ames-flow-bar" title={Math.round(data.progress) + '%'}>
          <div className="ames-flow-bar-fill" style={{ width: Math.max(0, Math.min(100, data.progress)) + '%', background: accent }} />
        </div>
      ) : null}
      {data.href ? <a className="ames-flow-link" href={data.href}>{data.linkText || '열기'}</a> : null}
      <Handle type="source" position={Position.Right} style={{ background: accent }} />
      <Handle id="b" type="source" position={Position.Bottom} style={{ background: accent }} />
    </div>
  );
}
const nodeTypes = { wo: WoNode };

function theme() { return document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark'; }

const toNode = (n, prev, hostId) => ({
  id: n.id, type: 'wo', position: prev ? prev.position : { x: n.x, y: n.y },   // 사용자가 옮긴 자리를 유지
  data: { kind: n.kind, kindLabel: n.kindLabel, title: n.title, lines: n.lines, status: n.status, statusLabel: n.statusLabel,
          progress: n.progress, operatorRows: n.operatorRows, chips: n.chips, hostId, href: n.href, linkText: n.linkText },
  zIndex: n.status === 'cur' ? 10 : 0,
});
const toEdge = e => ({
  id: e.id, source: e.source, target: e.target, sourceHandle: e.sourceHandle || undefined, targetHandle: e.targetHandle || undefined,
  animated: !!e.animated, label: e.label || undefined,
  markerEnd: { type: MarkerType.ArrowClosed, width: EDGE.arrowSize, height: EDGE.arrowSize },
  style: e.animated ? { strokeWidth: EDGE.animatedWidth, stroke: EDGE.animatedColor }
       : (EDGE.width || EDGE.color ? { strokeWidth: EDGE.width, stroke: EDGE.color } : undefined),
});

const handles = new Map();   // elementId -> { setGraph }
const dotnetRefs = new Map();   // elementId -> Blazor DotNetObjectReference — LOT 칩을 누르면 OpenLot(lotCode) 를 부른다
const CHIPS_SHOWN = 6;          // LOT 칩을 처음 몇 개까지 보일지(나머지는 +n 으로 펼침)

function Flow({ elementId, graph, labels }) {
  const [nodes, setNodes, onNodesChange] = useNodesState(graph.nodes.map(n => toNode(n, undefined, elementId)));
  const [edges, setEdges, onEdgesChange] = useEdgesState(graph.edges.map(toEdge));
  const { fitView } = useReactFlow();
  const [fs, setFs] = useState(false);
  const curIds = useRef([]);
  curIds.current = nodes.filter(n => n.data.status === 'cur').map(n => ({ id: n.id }));

  useEffect(() => {
    handles.set(elementId, {
      setGraph: g => {
        setNodes(prev => g.nodes.map(n => toNode(n, prev.find(p => p.id === n.id), elementId)));
        setEdges(g.edges.map(toEdge));
      },
    });
    return () => handles.delete(elementId);
  }, [elementId, setNodes, setEdges]);

  // 전체화면 — 흐름 영역(호스트 div)만 브라우저 전체화면으로 올린다. ESC·버튼으로 나오면 화면을 다시 맞춘다
  useEffect(() => {
    const on = () => {
      const host = document.getElementById(elementId);
      setFs(!!host && document.fullscreenElement === host);
      setTimeout(() => fitView(fitOpts()), 120);
    };
    document.addEventListener('fullscreenchange', on);
    return () => document.removeEventListener('fullscreenchange', on);
  }, [elementId, fitView]);

  // ESC — 전체화면이면 전체화면만 해제하고, 같은 키가 뒤의 팝업(Radzen 대화상자)을 닫지 않게 막는다.
  // 브라우저가 ESC 로 전체화면을 먼저 풀어도 곧바로 오는 키 이벤트를 짧게 삼켜 팝업이 같이 닫히지 않게 한다.
  useEffect(() => {
    let exitedAt = 0;
    const onFs = () => { if (!document.fullscreenElement) exitedAt = Date.now(); };
    const onKey = e => {
      if (e.key !== 'Escape') return;
      const host = document.getElementById(elementId);
      if (host && document.fullscreenElement === host) {
        e.preventDefault(); e.stopImmediatePropagation();
        document.exitFullscreen?.();
      } else if (Date.now() - exitedAt < 400) {
        e.preventDefault(); e.stopImmediatePropagation();
      }
    };
    document.addEventListener('fullscreenchange', onFs);
    window.addEventListener('keydown', onKey, true);
    return () => { document.removeEventListener('fullscreenchange', onFs); window.removeEventListener('keydown', onKey, true); };
  }, [elementId]);

  // 호스트 크기가 바뀌면(팝업 크기 조정·처음 배치) 화면을 다시 맞춘다 — 처음 작은 크기로 맞춰져 잘리는 것을 막는다
  useEffect(() => {
    const host = document.getElementById(elementId);
    if (!host || typeof ResizeObserver === 'undefined') return;
    let t;
    const ro = new ResizeObserver(() => { clearTimeout(t); t = setTimeout(() => fitView(fitOpts({ duration: 200 })), 150); });
    ro.observe(host);
    return () => { clearTimeout(t); ro.disconnect(); };
  }, [elementId, fitView]);

  // 처음 열 때도 「전체 보기」 버튼과 같은 크기로 맞춘다 — 팝업이 열리며 크기가 잡히는 동안을 기다려 두 번 맞춘다
  useEffect(() => {
    const ts = INITIAL_FIT_DELAYS_MS.map((d, i) => setTimeout(() => fitView(fitOpts({ duration: i === 0 ? 0 : 200 })), d));
    return () => ts.forEach(clearTimeout);
  }, [fitView]);

  const toggleFullscreen = useCallback(() => {
    const host = document.getElementById(elementId);
    if (!host) return;
    if (document.fullscreenElement === host) document.exitFullscreen?.();
    else host.requestFullscreen?.();
  }, [elementId]);

  const focusRunning = useCallback(() => {
    fitView(curIds.current.length
      ? { nodes: curIds.current, padding: FIT_RUNNING.padding, duration: 500, maxZoom: FIT_RUNNING.maxZoom }
      : { padding: FIT_RUNNING.fallbackPadding, duration: 500 });
  }, [fitView]);

  return (
    <ReactFlow nodes={nodes} edges={edges} onNodesChange={onNodesChange} onEdgesChange={onEdgesChange}
               nodeTypes={nodeTypes} fitView fitViewOptions={{ padding: FIT.padding }} colorMode={theme()}
               nodesDraggable={true} panOnDrag={true} zoomOnScroll={true} zoomOnPinch={true} zoomOnDoubleClick={true}
               nodesConnectable={false} elementsSelectable={true} minZoom={ZOOM.min} maxZoom={ZOOM.max}
               proOptions={{ hideAttribution: false }}>
      {GRID.show ? <Background variant={GRID.variant} gap={GRID.gap} color={GRID.color} size={GRID.size} /> : null}
      <Controls showInteractive={false} />
      {MINIMAP.show ? <MiniMap pannable zoomable position={MINIMAP.position} style={{ width: MINIMAP.width, height: MINIMAP.height }} maskColor={MINIMAP.maskColor} nodeColor={n => STATUS_COLOR[n.data?.status] ?? KIND_COLOR[n.data?.kind] ?? '#64748b'} /> : null}
      <Panel position="top-left" className="ames-flow-panel">
        <button type="button" className="ames-flow-btn" onClick={() => fitView(fitOpts({ duration: 400 }))}>{labels.fit}</button>
        <button type="button" className="ames-flow-btn ames-flow-btn-cur" onClick={focusRunning}>● {labels.running}</button>
        <button type="button" className="ames-flow-btn" onClick={toggleFullscreen}>{fs ? ('⤢ ' + labels.exitFullscreen) : ('⛶ ' + labels.fullscreen)}</button>
        <span className="ames-flow-hint">{labels.hint}</span>
      </Panel>
    </ReactFlow>
  );
}

const roots = new Map();

export function render(elementId, graph, labels, dotnetRef) {
  const el = document.getElementById(elementId);
  if (!el) return;
  dispose(elementId);   // dispose 가 참조도 지우므로 그 다음에 Blazor 참조를 등록한다
  if (dotnetRef) dotnetRefs.set(elementId, dotnetRef);
  const root = createRoot(el);
  roots.set(elementId, root);
  root.render(<ReactFlowProvider><Flow elementId={elementId} graph={graph} labels={labels || {}} /></ReactFlowProvider>);
}

// 실시간 갱신 — 노드 데이터·간선만 바꾸고 사용자가 옮긴 위치·화면은 그대로 둔다
export function update(elementId, graph) {
  const h = handles.get(elementId);
  if (h) h.setGraph(graph);
}

export function dispose(elementId) {
  const r = roots.get(elementId);
  if (r) { try { r.unmount(); } catch { /* 이미 사라진 요소 */ } roots.delete(elementId); }
  handles.delete(elementId);
  dotnetRefs.delete(elementId);
}
