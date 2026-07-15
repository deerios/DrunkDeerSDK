// Drives the on-screen keyboard's hot path. Blazor renders the board structure once
// (one .key per KeyInfo); this module owns everything that changes at poll rate so the
// Blazor render tree is never touched per frame — see FUTURE_PLAN §5.4.
//
// Per frame we pull the live per-key travel from .NET (a float[] indexed by firmware
// slot) and patch only the CSS custom properties of keys whose depth changed. Layout
// (px-per-unit) is recomputed from the container width via a ResizeObserver, not per frame.
//
// This module also owns the pointer gestures that drive selection, and — importantly — it
// owns the `class` attribute of every .key. Blazor renders each key's class once and must
// never re-render it: the rAF loop sets `.pressed` out-of-band, so a Blazor re-render would
// diff against a tree that has never heard of `.pressed` and silently strip it. Selection
// therefore arrives here via setSelection() rather than through the render tree.

const boards = new WeakMap();

// A drag shorter than this (in px) is a click, not a marquee.
const DRAG_THRESHOLD = 4;

// Mirrors DrunkDeer.Web.Services.SelectionMode.
const MODE_REPLACE = 'Replace';
const MODE_ADD = 'Add';
const MODE_TOGGLE = 'Toggle';

function modeFor(ev) {
    if (ev.ctrlKey || ev.metaKey) return MODE_TOGGLE;
    if (ev.shiftKey) return MODE_ADD;
    return MODE_REPLACE;
}

// Attach the rAF loop + resize handling to a board element.
//   boardEl   the .board container Blazor rendered
//   dotNetRef DotNetObjectReference exposing SampleHeights() -> float[] (by slot)
//   maxDepth  full-travel depth in mm (session.MaxDepthMm), maps depth -> 0..1
//   actDepth  actuation depth in mm; a key past this reads as "pressed"
export function attach(boardEl, dotNetRef, maxDepth, actDepth) {
    detach(boardEl);

    // slot -> { el, last, rect } so the loop can skip keys that didn't move and gestures can
    // hit-test without touching the DOM. The rect is in KLE units (as authored in the
    // geometry), scaled by --u only at test time, so a resize needs no recompute.
    const keys = [];
    boardEl.querySelectorAll('.key[data-slot]').forEach(el => {
        const slot = parseInt(el.getAttribute('data-slot'), 10);
        const u = prop => parseFloat(el.style.getPropertyValue(prop)) || 0;
        keys[slot] = {
            el,
            last: -1,
            pressed: false,
            // Primary rect only: the secondary leg of an ISO Enter is a small sliver, and
            // ignoring it costs nothing a user would notice when marquee-selecting.
            rect: { x: u('--kx'), y: u('--ky'), w: u('--kw'), h: u('--kh') },
        };
    });

    const state = { boardEl, dotNetRef, keys, maxDepth: maxDepth || 4, actDepth: actDepth || 1, rafId: 0, running: true };

    // Recompute px-per-unit from the rendered width and expose it as --u; the board's
    // height follows from its row count so the aspect ratio stays correct at any width.
    const cols = parseFloat(boardEl.style.getPropertyValue('--cols')) || 1;
    const rows = parseFloat(boardEl.style.getPropertyValue('--rows')) || 1;
    const relayout = () => {
        const u = boardEl.clientWidth / cols;
        boardEl.style.setProperty('--u', u + 'px');
        boardEl.style.height = (rows * u) + 'px';
    };
    const ro = new ResizeObserver(relayout);
    ro.observe(boardEl);
    relayout();
    state.ro = ro;

    const frame = () => {
        if (!state.running) return;
        let heights;
        try {
            heights = dotNetRef.invokeMethod('SampleHeights');
        } catch {
            // Session went away between frames (disconnect races the loop) — stop quietly.
            state.running = false;
            return;
        }
        if (heights) {
            const inv = 1 / state.maxDepth;
            for (let slot = 0; slot < heights.length; slot++) {
                const k = keys[slot];
                if (!k) continue;
                const mm = heights[slot];
                if (mm === k.last) continue;
                k.last = mm;
                const frac = mm <= 0 ? 0 : (mm >= state.maxDepth ? 1 : mm * inv);
                k.el.style.setProperty('--depth', frac.toFixed(3));
                k.el.style.setProperty('--glow', frac.toFixed(3));
                const pressed = mm >= state.actDepth;
                if (pressed !== k.pressed) {
                    k.pressed = pressed;
                    k.el.classList.toggle('pressed', pressed);
                }
            }
        }
        state.rafId = requestAnimationFrame(frame);
    };

    attachSelection(state);

    boards.set(boardEl, state);
    state.rafId = requestAnimationFrame(frame);
}

// Pointer gestures: click a key to select it, drag across the board to marquee-select,
// click empty board to clear. Shift adds, ctrl/cmd toggles. The gesture reports the hit
// slots to .NET, which owns the selection; the resulting classes come back via
// setSelection() so the store stays the single source of truth.
function attachSelection(state) {
    const { boardEl, keys } = state;

    const unitsAt = ev => {
        const u = parseFloat(boardEl.style.getPropertyValue('--u')) || 1;
        const box = boardEl.getBoundingClientRect();
        return { x: (ev.clientX - box.left) / u, y: (ev.clientY - box.top) / u };
    };

    const hitsIn = (a, b) => {
        const x1 = Math.min(a.x, b.x), x2 = Math.max(a.x, b.x);
        const y1 = Math.min(a.y, b.y), y2 = Math.max(a.y, b.y);
        const hits = [];
        keys.forEach((k, slot) => {
            const r = k.rect;
            if (r.x < x2 && r.x + r.w > x1 && r.y < y2 && r.y + r.h > y1) hits.push(slot);
        });
        return hits;
    };

    // Blazor renders the marquee (see the component) so that scoped CSS applies to it.
    const marquee = boardEl.querySelector('.marquee');
    if (!marquee) return;
    state.marquee = marquee;

    const drawMarquee = (a, b) => {
        const u = parseFloat(boardEl.style.getPropertyValue('--u')) || 1;
        marquee.style.left = (Math.min(a.x, b.x) * u) + 'px';
        marquee.style.top = (Math.min(a.y, b.y) * u) + 'px';
        marquee.style.width = (Math.abs(b.x - a.x) * u) + 'px';
        marquee.style.height = (Math.abs(b.y - a.y) * u) + 'px';
    };

    let drag = null;

    const onPointerDown = ev => {
        if (ev.button !== 0) return;
        drag = { start: unitsAt(ev), moved: false, mode: modeFor(ev) };
        boardEl.setPointerCapture(ev.pointerId);
    };

    const onPointerMove = ev => {
        if (!drag) return;
        const at = unitsAt(ev);
        const u = parseFloat(boardEl.style.getPropertyValue('--u')) || 1;
        if (!drag.moved) {
            const dx = (at.x - drag.start.x) * u, dy = (at.y - drag.start.y) * u;
            if (Math.hypot(dx, dy) < DRAG_THRESHOLD) return;
            drag.moved = true;
            marquee.hidden = false;
        }
        drawMarquee(drag.start, at);
    };

    const onPointerUp = ev => {
        if (!drag) return;
        const at = unitsAt(ev);
        const hits = drag.moved
            ? hitsIn(drag.start, at)
            : hitsIn(drag.start, drag.start); // a click is a zero-area marquee
        marquee.hidden = true;

        // A plain click on empty board clears; a modified one leaves the selection alone
        // (the user is mid-refinement and just missed).
        const isEmptyClick = hits.length === 0 && !drag.moved;
        const mode = drag.mode;
        drag = null;
        if (isEmptyClick && mode !== MODE_REPLACE) return;

        state.dotNetRef.invokeMethodAsync('SelectSlots', hits, mode);
    };

    const onKeyDown = ev => {
        // Clicking bare board also clears, but the only uncovered space on a 75% layout is
        // the slivers between the F-key groups — too small to be the only way out.
        if (ev.key === 'Escape') {
            ev.preventDefault();
            state.dotNetRef.invokeMethodAsync('SelectSlots', [], MODE_REPLACE);
            return;
        }
        if (ev.key !== 'Enter' && ev.key !== ' ') return;
        const el = ev.target.closest?.('.key[data-slot]');
        if (!el || !boardEl.contains(el)) return;
        ev.preventDefault();
        const slot = parseInt(el.getAttribute('data-slot'), 10);
        state.dotNetRef.invokeMethodAsync('SelectSlots', [slot], modeFor(ev));
    };

    boardEl.addEventListener('pointerdown', onPointerDown);
    boardEl.addEventListener('pointermove', onPointerMove);
    boardEl.addEventListener('pointerup', onPointerUp);
    boardEl.addEventListener('pointercancel', onPointerUp);
    boardEl.addEventListener('keydown', onKeyDown);
    state.detachSelection = () => {
        boardEl.removeEventListener('pointerdown', onPointerDown);
        boardEl.removeEventListener('pointermove', onPointerMove);
        boardEl.removeEventListener('pointerup', onPointerUp);
        boardEl.removeEventListener('pointercancel', onPointerUp);
        boardEl.removeEventListener('keydown', onKeyDown);
    };
}

// Pushes the authoritative selection from .NET onto the DOM. Called on every selection
// change — rare enough that touching all keys is cheaper than tracking deltas.
export function setSelection(boardEl, slots) {
    const state = boards.get(boardEl);
    if (!state) return;
    const wanted = new Set(slots);
    state.keys.forEach((k, slot) => {
        const on = wanted.has(slot);
        if (k.selected === on) return;
        k.selected = on;
        k.el.classList.toggle('selected', on);
        k.el.setAttribute('aria-pressed', on ? 'true' : 'false');
    });
}

// Pushes the per-key backlight colours from .NET onto the DOM. Called after a lighting write
// (rare), so like setSelection it just touches every key rather than tracking deltas.
//   entries  [{ slot, kc, lc, lit }] — kc is the backlight colour driving the glow, lc the
//            (readable) legend colour, lit 0/1 for whether the key is backlit at rest.
export function setColors(boardEl, entries) {
    const state = boards.get(boardEl);
    if (!state) return;
    for (const { slot, kc, lc, lit } of entries) {
        const k = state.keys[slot];
        if (!k) continue;
        k.el.style.setProperty('--kc', kc);
        k.el.style.setProperty('--lc', lc);
        k.el.style.setProperty('--lit', lit);
    }
}

// Pushes the per-key actuation markers from .NET onto the DOM. Called on a lighting-style
// cadence — a write, a slider move, a selection change — never per frame.
//   entries  [{ slot, act, state }] — act is the marker position as a fraction of full travel,
//            state is 'unknown' | 'set' | 'preview', naming a --act-* colour the CSS defines.
export function setActuation(boardEl, entries) {
    const state = boards.get(boardEl);
    if (!state) return;
    for (const { slot, act, state: kind } of entries) {
        const k = state.keys[slot];
        if (!k) continue;
        k.el.style.setProperty('--act', act);
        k.el.style.setProperty('--actc', `var(--act-${kind})`);
    }
}

export function detach(boardEl) {
    const state = boards.get(boardEl);
    if (!state) return;
    state.running = false;
    if (state.rafId) cancelAnimationFrame(state.rafId);
    if (state.ro) state.ro.disconnect();
    if (state.detachSelection) state.detachSelection();
    boards.delete(boardEl);
}
