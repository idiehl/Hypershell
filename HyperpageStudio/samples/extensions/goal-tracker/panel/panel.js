const protocol = 'hypershell.panel';
const protocolVersion = 1;
const stageList = document.querySelector('#stage-list');
const progress = document.querySelector('.progress');
const progressSummary = document.querySelector('#progress-summary');
const trackerTitle = document.querySelector('#tracker-title');
const activeTab = document.querySelector('#active-tab');
const hostStatus = document.querySelector('#host-status');
const signal = document.querySelector('.signal');
const webview = window.chrome?.webview;
const pendingRequests = new Map();
let nextRequestNumber = 0;
let hostMethods = new Set();
let selectedStageIndex = 2;

function setConnectionStatus(text, state = 'ready') {
  hostStatus.textContent = text;
  signal.dataset.state = state;
}

function renderProgress(index) {
  const stages = [...stageList.children];
  selectedStageIndex = Math.max(0, Math.min(index, stages.length - 1));
  stages.forEach((item, stageIndex) => {
    const state = stageIndex < selectedStageIndex
      ? 'complete'
      : stageIndex === selectedStageIndex
        ? 'active'
        : 'pending';
    item.dataset.state = state;
    item.querySelector('.marker').textContent = state === 'complete'
      ? '✓'
      : String(stageIndex + 1);
  });

  progress.setAttribute('aria-valuenow', String(selectedStageIndex));
  progress.dataset.progress = String(selectedStageIndex);
  progressSummary.textContent = `${selectedStageIndex} of ${stages.length} stages complete`;
}

function applyHostContext(context) {
  const project = context?.workspace?.activeProject;
  const tab = context?.workspace?.activeTab;
  trackerTitle.textContent = project?.name || 'No active project';
  activeTab.textContent = tab
    ? `${tab.title} · ${tab.kind}`
    : 'No active terminal or document';

  const theme = context?.theme;
  if (theme) {
    document.documentElement.dataset.colorScheme = theme.colorScheme;
  }
}

function request(method, params = {}) {
  if (!webview) {
    return Promise.reject(new Error('Hypershell host messaging is unavailable.'));
  }

  const id = `goal-${Date.now().toString(36)}-${++nextRequestNumber}`;
  return new Promise((resolve, reject) => {
    const timeout = window.setTimeout(() => {
      pendingRequests.delete(id);
      reject(new Error(`The host did not respond to ${method}.`));
    }, 5000);
    pendingRequests.set(id, { resolve, reject, timeout });
    webview.postMessage({
      protocol,
      version: protocolVersion,
      type: 'request',
      id,
      method,
      params
    });
  });
}

if (webview) {
  webview.addEventListener('message', (event) => {
    const message = event.data;
    if (!message || message.protocol !== protocol || message.version !== protocolVersion) {
      return;
    }

    if (message.type === 'event' && message.event === 'host.contextChanged') {
      applyHostContext(message.data);
      return;
    }

    if (message.type !== 'response' || !pendingRequests.has(message.id)) {
      return;
    }

    const pending = pendingRequests.get(message.id);
    pendingRequests.delete(message.id);
    window.clearTimeout(pending.timeout);
    if (message.ok) {
      pending.resolve(message.result);
    } else {
      pending.reject(new Error(message.error?.message || 'The host rejected the request.'));
    }
  });
}

stageList.addEventListener('click', async (event) => {
  const stage = event.target.closest('li');
  if (!stage) return;

  const stages = [...stageList.children];
  renderProgress(stages.indexOf(stage));
  if (!hostMethods.has('state.set')) return;

  setConnectionStatus('Saving progress…', 'pending');
  try {
    await request('state.set', {
      value: {
        schemaVersion: 1,
        selectedStageIndex,
        updatedAtUtc: new Date().toISOString()
      }
    });
    setConnectionStatus('Host connected · progress saved on this device');
  } catch (error) {
    setConnectionStatus(error.message, 'error');
  }
});

async function initialize() {
  renderProgress(selectedStageIndex);
  if (!webview) {
    trackerTitle.textContent = 'Host API unavailable';
    setConnectionStatus('This sample requires Hypershell panel API v1.', 'error');
    return;
  }

  try {
    const handshake = await request('host.ready');
    hostMethods = new Set(handshake.methods || []);
    if (hostMethods.has('state.get')) {
      const stored = await request('state.get');
      if (Number.isInteger(stored?.value?.selectedStageIndex)) {
        renderProgress(stored.value.selectedStageIndex);
      }
    }

    if (hostMethods.has('host.getContext')) {
      applyHostContext(await request('host.getContext'));
    } else {
      trackerTitle.textContent = 'Workspace metadata not approved';
      activeTab.textContent = 'State storage remains available';
    }

    if (hostMethods.has('events.subscribe')) {
      await request('events.subscribe', { events: ['host.contextChanged'] });
    }

    setConnectionStatus('Host connected · device-local state');
  } catch (error) {
    trackerTitle.textContent = 'Host connection failed';
    setConnectionStatus(error.message, 'error');
  }
}

initialize();
