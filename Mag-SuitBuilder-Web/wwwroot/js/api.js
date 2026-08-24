// Fetch wrappers + the SSE lifecycle for search events.

async function json(url, options) {
  const response = await fetch(url, options);
  const body = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) {
    const message = body?.error ?? (response.status + ' ' + response.statusText);
    throw new Error(message);
  }
  return body;
}

export const api = {
  inventory: () => json('/api/inventory'),
  reloadInventory: (rootPath = null) => json('/api/inventory/load', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ rootPath }),
  }),
  cantrips: () => json('/api/cantrips'),
  setFlags: (itemKey, flags) => json(`/api/items/${itemKey}/flags`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(flags),
  }),
  startSearch: request => json('/api/search', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  }),
  stopSearch: () => json('/api/search/stop', { method: 'POST' }),
  status: () => json('/api/search/status'),
};

let eventSource = null;

export function openEvents(handlers) {
  closeEvents();
  eventSource = new EventSource('/api/search/events');

  for (const type of ['snapshot', 'suit', 'suit-evicted', 'progress', 'warning', 'completed']) {
    eventSource.addEventListener(type, event => {
      let data = null;
      try { data = JSON.parse(event.data); } catch { return; }
      handlers[type]?.(data);
    });
  }

  eventSource.onerror = () => handlers.error?.();
  return eventSource;
}

export function closeEvents() {
  if (eventSource) {
    eventSource.close();
    eventSource = null;
  }
}
