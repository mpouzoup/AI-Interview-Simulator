export function scrollToLatestMessage(containerId) {
    const container = document.getElementById(containerId);
    if (!container) return;

    // This fixed-height panel owns the chat scroll; do not scroll the document.
    container.scrollTop = container.scrollHeight;
}

export function focusAnswer(inputId) {
    const input = document.getElementById(inputId);
    if (!input || input.tagName !== 'TEXTAREA' || !input.isConnected ||
        input.disabled || input.readOnly || input.getClientRects().length === 0) return false;

    input.focus({ preventScroll: true });
    return document.activeElement === input;
}

// These observations approximate display and elapsed wall time, not reading or attention.
// performance.now avoids mixing the browser clock with API/UI server clocks.
const stageTimings = new Map();
export async function markPhase(key, phase) {
    await new Promise(resolve => {
        const timer = setTimeout(resolve, 100); // background tabs may suspend animation frames
        requestAnimationFrame(() => { clearTimeout(timer); resolve(); });
    });
    let stage = stageTimings.get(key);
    if (!stage) {
        stage = { phases: new Map(), events: new Map() };
        stageTimings.set(key, stage);
    }
    if (!stage.phases.has(phase))
        stage.phases.set(phase, { mono: performance.now(), wall: new Date().toISOString() });
    const shown = stage.phases.get(phase);
    const wait = stage.phases.get(phase === 'feedback2' ? 'wait2' : 'wait1');
    return { browserOccurredAt: shown.wall,
        waitMilliseconds: phase.startsWith('feedback') && wait ? Math.max(0, shown.mono - wait.mono) : null };
}

export function captureTiming(key, event) {
    const stage = stageTimings.get(key);
    if (!stage) return null;
    if (stage.events.has(event)) return stage.events.get(event);
    const now = performance.now();
    const wall = new Date().toISOString();
    const phase = { submit: 'question', decision: 'feedback1', revision: 'revision', continue: 'feedback2' }[event];
    const start = stage.phases.get(phase);
    const question = stage.phases.get('question');
    const result = { browserOccurredAt: wall,
        elapsedMilliseconds: start ? Math.max(0, now - start.mono) : null,
        stageElapsedMilliseconds: question ? Math.max(0, now - question.mono) : null };
    stage.events.set(event, result); // retries reuse the first observation
    if (event === 'submit' || event === 'revision')
        stage.phases.set(event === 'submit' ? 'wait1' : 'wait2', { mono: now, wall });
    return result;
}


// Tab-scoped resume token only; unsent drafts and questionnaire/demographic data are not stored.
const sessionKey = 'interview-session-v2';
export function storeSession(session) {
    try { sessionStorage.setItem(sessionKey, JSON.stringify(session)); return true; }
    catch { return false; }
}
export function loadSession() {
    try {
        const value = JSON.parse(sessionStorage.getItem(sessionKey) ?? 'null');
        return value?.flowVersion === 2 && typeof value.id === 'string' ? value : null;
    } catch { return null; }
}


export async function loadStudy() {
    const response = await fetch('/participant/bootstrap', { credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok) return null;
    const info = await response.json();
    return { ...info.study, signedIn: info.signedIn };
}
export async function registerParticipant(body) {
    const bootstrap = await fetch('/participant/bootstrap', { credentials: 'same-origin', cache: 'no-store' });
    if (!bootstrap.ok) return false;
    const info = await bootstrap.json();
    const response = await fetch('/participant/register', { method: 'POST', credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', 'X-Interview-CSRF': info.csrfToken }, body: JSON.stringify(body) });
    if (!response.ok) return false;
    try { sessionStorage.removeItem(sessionKey); } catch { }
    location.reload(); // New circuit reads the HttpOnly authenticated cookie.
    return true;
}
export async function revokeParticipant() {
    if (!confirm('Να τερματιστεί η πρόσβαση; Τα ήδη αποθηκευμένα δεδομένα δεν διαγράφονται.')) return true;
    const bootstrap = await fetch('/participant/bootstrap', { credentials: 'same-origin', cache: 'no-store' });
    if (!bootstrap.ok) return false;
    const info = await bootstrap.json();
    const response = await fetch('/participant/logout', { method: 'POST', credentials: 'same-origin',
        headers: { 'X-Interview-CSRF': info.csrfToken } });
    if (!response.ok) return false;
    try { sessionStorage.removeItem(sessionKey); } catch { }
    location.reload();
    return true;
}
