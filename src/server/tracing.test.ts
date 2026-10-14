import assert from "node:assert/strict";
import { test } from "node:test";
import { trace } from "@opentelemetry/api";
import { BasicTracerProvider, InMemorySpanExporter, SimpleSpanProcessor } from "@opentelemetry/sdk-trace-base";
import { RunEventLog } from "../telemetry/events.js";
import { initTelemetry, tracedRecorder, withRunSpan } from "../telemetry/tracing.js";

const exporter = new InMemorySpanExporter();
trace.setGlobalTracerProvider(new BasicTracerProvider({ spanProcessors: [new SimpleSpanProcessor(exporter)] }));

test("run span parents phase and tool spans without leaking content", async () => {
  exporter.reset();
  await withRunSpan("run-1", async (ctx) => {
    const recorder = tracedRecorder(new RunEventLog(), ctx);
    const phase = recorder.begin({ phase: "planning", summary: "Secret requirement text" });
    const tool = recorder.begin({ phase: "tool", toolName: "create_feature", summary: "Feature: confidential title" });
    recorder.finish(tool, { status: "completed", ticketId: "DEMO-100", summary: "Feature: confidential title" });
    recorder.finish(phase, { status: "completed" });
    return { status: "completed" };
  }, (result) => result);

  const spans = exporter.getFinishedSpans();
  const root = spans.find((span) => span.name === "agent.run");
  assert.ok(root);
  assert.equal(root.attributes["agent.run.status"], "completed");
  const children = spans.filter((span) => span !== root);
  assert.deepEqual(children.map((span) => span.name).sort(), ["agent.planning", "tool create_feature"]);
  for (const child of children) assert.equal(child.parentSpanContext?.spanId, root.spanContext().spanId);
  assert.equal(children.find((span) => span.name === "tool create_feature")?.attributes["agent.ticket.id"], "DEMO-100");
  assert.ok(!JSON.stringify(spans.map((span) => span.attributes)).match(/Secret|confidential/));
});

test("failed events and run errors mark spans as errors", async () => {
  exporter.reset();
  await assert.rejects(withRunSpan("run-2", async (ctx) => {
    const recorder = tracedRecorder(new RunEventLog(), ctx);
    recorder.finish(recorder.begin({ phase: "assessment", summary: "x" }), { status: "failed" });
    throw new Error("boom");
  }, () => ({ status: "x" })), /boom/);
  const spans = exporter.getFinishedSpans();
  assert.equal(spans.find((span) => span.name === "agent.assessment")?.status.code, 2);
  assert.equal(spans.find((span) => span.name === "agent.run")?.attributes["agent.run.status"], "failed");
});

test("telemetry stays off without a connection string", async () => {
  assert.equal(await initTelemetry({}), false);
});
