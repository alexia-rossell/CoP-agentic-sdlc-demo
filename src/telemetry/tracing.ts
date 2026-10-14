import { context, SpanStatusCode, trace, type Context, type Span } from "@opentelemetry/api";
import type { RunRecorder } from "./events.js";

const SCOPE = "agentic-sdlc-demo";

// Spans carry only phase, tool name, ticket ID and status. Requirement text, prompts,
// model output and event summaries are deliberately never attached.
export function tracedRecorder(inner: RunRecorder, parent: Context = context.active()): RunRecorder {
  const tracer = trace.getTracer(SCOPE);
  const spans = new Map<string, Span>();
  return {
    begin(event) {
      const id = inner.begin(event);
      const name = event.toolName ? `tool ${event.toolName}` : `agent.${event.phase}`;
      spans.set(id, tracer.startSpan(name, {
        attributes: { "agent.phase": event.phase, ...(event.toolName ? { "agent.tool.name": event.toolName } : {}) },
      }, parent));
      return id;
    },
    finish(id, patch) {
      inner.finish(id, patch);
      const span = spans.get(id);
      if (!span) return;
      spans.delete(id);
      span.setAttribute("agent.status", patch.status);
      if (patch.ticketId) span.setAttribute("agent.ticket.id", patch.ticketId);
      if (patch.status === "failed") span.setStatus({ code: SpanStatusCode.ERROR });
      span.end();
    },
  };
}

// Runs fn inside a root "agent.run" span; child spans created by tracedRecorder attach to it.
export async function withRunSpan<T>(
  runId: string,
  fn: (recorderContext: Context) => Promise<T>,
  outcome: (result: T) => { status: string },
): Promise<T> {
  const span = trace.getTracer(SCOPE).startSpan("agent.run", { attributes: { "agent.run.id": runId } });
  const ctx = trace.setSpan(context.active(), span);
  try {
    const result = await fn(ctx);
    const { status } = outcome(result);
    span.setAttribute("agent.run.status", status);
    return result;
  } catch (error) {
    span.setStatus({ code: SpanStatusCode.ERROR, message: error instanceof Error ? error.name : "error" });
    span.setAttribute("agent.run.status", "failed");
    throw error;
  } finally {
    span.end();
  }
}

// Enabled only when a connection string is configured, so local runs and tests export nothing.
export async function initTelemetry(env: NodeJS.ProcessEnv = process.env): Promise<boolean> {
  const connectionString = env.APPLICATIONINSIGHTS_CONNECTION_STRING;
  if (!connectionString) return false;
  const { useAzureMonitor } = await import("@azure/monitor-opentelemetry");
  useAzureMonitor({ azureMonitorExporterOptions: { connectionString } });
  return true;
}
