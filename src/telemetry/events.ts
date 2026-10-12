export type AgentRunEvent = {
  id: string;
  timestamp: string;
  phase: "assessment" | "clarification" | "planning" | "tool" | "review" | "report";
  toolName?: string;
  status: "pending" | "running" | "completed" | "reused" | "failed";
  summary: string;
  ticketId?: string;
  durationMs?: number;
};

export interface RunRecorder {
  begin(event: { phase: AgentRunEvent["phase"]; summary: string; toolName?: string }): string;
  finish(id: string, patch: { status: AgentRunEvent["status"]; summary?: string; ticketId?: string }): void;
}

export const shorten = (value: string, max = 90): string => {
  const flat = value.replace(/\s+/g, " ").trim();
  return flat.length > max ? `${flat.slice(0, max - 1)}…` : flat;
};

// Records only short summaries emitted by the application, never prompts or SDK internals.
export class RunEventLog implements RunRecorder {
  private readonly items: AgentRunEvent[] = [];
  private readonly started = new Map<string, number>();

  begin(event: { phase: AgentRunEvent["phase"]; summary: string; toolName?: string }): string {
    const id = `evt-${this.items.length + 1}`;
    this.items.push({
      id, timestamp: new Date().toISOString(), phase: event.phase, status: "running",
      summary: shorten(event.summary), ...(event.toolName ? { toolName: event.toolName } : {}),
    });
    this.started.set(id, performance.now());
    return id;
  }

  finish(id: string, patch: { status: AgentRunEvent["status"]; summary?: string; ticketId?: string }): void {
    const event = this.items.find((item) => item.id === id);
    const start = this.started.get(id);
    if (!event || start === undefined) return;
    event.status = patch.status;
    if (patch.summary) event.summary = shorten(patch.summary);
    if (patch.ticketId) event.ticketId = patch.ticketId;
    event.durationMs = Math.round(performance.now() - start);
    this.started.delete(id);
  }

  events(): AgentRunEvent[] {
    return this.items.map((item) => ({ ...item }));
  }
}
