/**
 * Stand-in for @microsoft/signalr in tests (mocked in setup.ts): no network, and tests can play
 * the server by calling `hub.emit('conversationChanged', { conversationId })`.
 */
type Handler = (...args: unknown[]) => void;

const handlers = new Map<string, Set<Handler>>();

export const hub = {
  started: 0,
  options: undefined as unknown,
  emit(event: string, ...args: unknown[]) {
    handlers.get(event)?.forEach((handler) => handler(...args));
  },
  reset() {
    handlers.clear();
    hub.started = 0;
    hub.options = undefined;
  },
};

class FakeConnection {
  on(event: string, handler: Handler) {
    const set = handlers.get(event) ?? new Set<Handler>();
    set.add(handler);
    handlers.set(event, set);
  }

  onreconnected(handler: Handler) {
    this.on('__reconnected', handler);
  }

  async start() {
    hub.started++;
  }

  async stop() {
    handlers.clear();
  }
}

export class HubConnectionBuilder {
  withUrl(_url: string, options: unknown) {
    hub.options = options;
    return this;
  }

  withAutomaticReconnect() {
    return this;
  }

  configureLogging() {
    return this;
  }

  build() {
    return new FakeConnection();
  }
}

export const LogLevel = { Warning: 3 };
