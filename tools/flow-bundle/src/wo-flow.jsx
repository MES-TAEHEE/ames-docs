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

const KIND_COLOR = { so: '#38bdf8', plan: '#a78bfa', wo: '#34d399', step: '#fbbf24', slot: '#60a5fa', result: '#f472b6' };
const STATUS_COLOR = { done: '#22c55e', cur: '#3b82f6', todo: '#94a3b8', bad: '#ef4444' };

function WoNode({ data }) {
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
      {data.operator ? <div className={'ames-flow-op' + (data.operatorLive ? ' ames-flow-op-live' : '')}>👤 {data.operator}</div> : null}
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

const toNode = (n, prev) => ({
  id: n.id, type: 'wo', position: prev ? prev.position : { x: n.x, y: n.y },   // 사용자가 옮긴 자리를 유지
  data: { kind: n.kind, kindLabel: n.kindLabel, title: n.title, lines: n.lines, status: n.status, statusLabel: n.statusLabel,
          progress: n.progress, operator: n.operator, operatorLive: n.operatorLive, href: n.href, linkText: n.linkText },
  zIndex: n.status === 'cur' ? 10 : 0,
});
const toEdge = e => ({
  id: e.id, source: e.source, target: e.target, sourceHandle: e.sourceHandle || undefined, targetHandle: e.targetHandle || undefined,
  animated: !!e.animated, label: e.label || undefined,
  markerEnd: { type: MarkerType.ArrowClosed },
  style: e.animated ? { strokeWidth: 3, stroke: STATUS_COLOR.cur } : undefined,
});

const handles = new Map();   // elementId -> { setGraph }

function Flow({ elementId, graph, labels }) {
  const [nodes, setNodes, onNodesChange] = useNodesState(graph.nodes.map(n => toNode(n)));
  const [edges, setEdges, onEdgesChange] = useEdgesState(graph.edges.map(toEdge));
  const { fitView } = useReactFlow();
  const [fs, setFs] = useState(false);
  const curIds = useRef([]);
  curIds.current = nodes.filter(n => n.data.status === 'cur').map(n => ({ id: n.id }));

  useEffect(() => {
    handles.set(elementId, {
      setGraph: g => {
        setNodes(prev => g.nodes.map(n => toNode(n, prev.find(p => p.id === n.id))));
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
      setTimeout(() => fitView({ padding: 0.15, duration: 300 }), 120);
    };
    document.addEventListener('fullscreenchange', on);
    return () => document.removeEventListener('fullscreenchange', on);
  }, [elementId, fitView]);

  // 호스트 크기가 바뀌면(팝업 크기 조정·처음 배치) 화면을 다시 맞춘다 — 처음 작은 크기로 맞춰져 잘리는 것을 막는다
  useEffect(() => {
    const host = document.getElementById(elementId);
    if (!host || typeof ResizeObserver === 'undefined') return;
    let t;
    const ro = new ResizeObserver(() => { clearTimeout(t); t = setTimeout(() => fitView({ padding: 0.15, duration: 200 }), 150); });
    ro.observe(host);
    return () => { clearTimeout(t); ro.disconnect(); };
  }, [elementId, fitView]);

  // 처음 열 때도 「전체 보기」 버튼과 같은 크기로 맞춘다 — 팝업이 열리며 크기가 잡히는 동안을 기다려 두 번 맞춘다
  useEffect(() => {
    const t1 = setTimeout(() => fitView({ padding: 0.08, duration: 0 }), 200);
    const t2 = setTimeout(() => fitView({ padding: 0.08, duration: 200 }), 700);
    return () => { clearTimeout(t1); clearTimeout(t2); };
  }, [fitView]);

  const toggleFullscreen = useCallback(() => {
    const host = document.getElementById(elementId);
    if (!host) return;
    if (document.fullscreenElement === host) document.exitFullscreen?.();
    else host.requestFullscreen?.();
  }, [elementId]);

  const focusRunning = useCallback(() => {
    fitView(curIds.current.length ? { nodes: curIds.current, padding: 0.9, duration: 500, maxZoom: 0.9 } : { padding: 0.5, duration: 500 });
  }, [fitView]);

  return (
    <ReactFlow nodes={nodes} edges={edges} onNodesChange={onNodesChange} onEdgesChange={onEdgesChange}
               nodeTypes={nodeTypes} fitView fitViewOptions={{ padding: 0.08 }} colorMode={theme()}
               nodesDraggable={true} panOnDrag={true} zoomOnScroll={true} zoomOnPinch={true} zoomOnDoubleClick={true}
               nodesConnectable={false} elementsSelectable={true} minZoom={0.2} maxZoom={1.8}
               proOptions={{ hideAttribution: false }}>
      <Background gap={18} />
      <Controls showInteractive={false} />
      <MiniMap pannable zoomable nodeColor={n => STATUS_COLOR[n.data?.status] ?? KIND_COLOR[n.data?.kind] ?? '#64748b'} />
      <Panel position="top-left" className="ames-flow-panel">
        <button type="button" className="ames-flow-btn" onClick={() => fitView({ padding: 0.08, duration: 400 })}>{labels.fit}</button>
        <button type="button" className="ames-flow-btn ames-flow-btn-cur" onClick={focusRunning}>● {labels.running}</button>
        <button type="button" className="ames-flow-btn" onClick={toggleFullscreen}>{fs ? ('⤢ ' + labels.exitFullscreen) : ('⛶ ' + labels.fullscreen)}</button>
        <span className="ames-flow-hint">{labels.hint}</span>
      </Panel>
    </ReactFlow>
  );
}

const roots = new Map();

export function render(elementId, graph, labels) {
  const el = document.getElementById(elementId);
  if (!el) return;
  dispose(elementId);
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
}
