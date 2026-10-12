import assert from "node:assert/strict";
import { test } from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import type { AddressInfo } from "node:net";
import type { Server } from "node:http";
import type { Tool } from "@github/copilot-sdk";
import { runAgent, type AgentClient, type AgentSession } from "../agent/run-agent.js";
import type { TicketStore } from "../storage/tickets.js";
import type { RunRecorder } from "../telemetry/events.js";
import type { RunState } from "./api-types.js";
import { createDemoServer, resolveDemoStorePath, type Runner } from "./app.js";
import { validateRun } from "./validation.js";

const feature = { title: "Claims intake", description: "Employee workflow", businessValue: "Faster review", tags: ["demo"] };
const story = (parentId: string) => ({
  parentId, title: "Upload a document", persona: "Employee", goal: "Upload a synthetic file",
  benefit: "Start a claim", acceptanceCriteria: ["Authenticated employees can upload"],
});
const task = (parentId: string, category: string) => ({
  parentId, title: `${category} work`, description: `Deliver ${category}`, category, priority: "high",
});
const questions = { questions: [{ topic: "users", question: "Who uses it?", reason: "Needed for stories" }] };

const session = (send: (...args: unknown[]) => Promise<unknown>): AgentSession => ({
  sendAndWait: send as AgentSession["sendAndWait"], disconnect: async () => {}, abort: async () => {},
});
const invoke = async (tools: Tool[], name: string, args: unknown) =>
  tools.find((tool) => tool.name === name)!.handler!(args, { sessionId: "t", toolCallId: "t", toolName: name, arguments: args }) as Promise<{ id: string }>;

// Scripted sessions drive the real agent and tool handlers; no Copilot runtime is used.
const incompleteClient: AgentClient = {
  createSession: async (config) => session(async () => {
    await invoke(config.tools!, "request_missing_information", questions);
    return { ready: false, reason: "Missing users" };
  }),
};
const makeCompleteClient = (): AgentClient => ({
  createSession: (() => {
    let count = 0;
    return async (config) => {
      if (++count === 1) return session(async () => ({ ready: true, reason: "Complete" }));
      let turn = 0;
      return session(async () => {
        if (++turn === 1) {
          const f = await invoke(config.tools!, "create_feature", feature);
          const s = await invoke(config.tools!, "create_user_story", story(f.id));
          for (const category of ["implementation", "security", "testing", "observability", "operations"]) {
            await invoke(config.tools!, "create_engineering_task", task(s.id, category));
          }
        }
        return { data: { content: "ok" } };
      });
    };
  })(),
});

const scriptedRunner = (client: () => AgentClient): Runner => (request, store: TicketStore, recorder: RunRecorder) =>
  runAgent(request, store, client(), undefined, recorder);

async function withServer(
  runner: Runner, run: (base: string, dir: string) => Promise<void>, storeName = "tickets.json",
) {
  const dir = await mkdtemp(join(tmpdir(), "agentic-web-test-"));
  const server: Server = createDemoServer({
    storePath: join(dir, storeName), dataDir: dir, runner,
    examples: async () => [{ id: "incomplete", label: "Incomplete example", request: "Build an AI application for claims." },
      { id: "complete", label: "Complete example", request: "Complete synthetic request" }],
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  try {
    await run(`http://127.0.0.1:${(server.address() as AddressInfo).port}`, dir);
  } finally {
    await new Promise((resolve) => server.close(resolve));
    await rm(dir, { recursive: true, force: true });
  }
}

const post = (url: string, body: unknown) => fetch(url, {
  method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
});
async function finished(base: string, runId: string): Promise<RunState> {
  for (let i = 0; i < 200; i++) {
    const state = await (await fetch(`${base}/api/runs/${runId}`)).json() as RunState;
    if (state.status !== "running") return state;
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  throw new Error("Run did not finish");
}

test("empty and malformed run requests are rejected without starting a run", async () => {
  await withServer(scriptedRunner(makeCompleteClient), async (base) => {
    assert.equal((await post(`${base}/api/runs`, { request: "   " })).status, 400);
    assert.equal((await post(`${base}/api/runs`, {})).status, 400);
    assert.equal((await post(`${base}/api/runs`, { request: "x", extra: 1 })).status, 400);
    const text = await fetch(`${base}/api/runs`, { method: "POST", body: "{}" });
    assert.equal(text.status, 415);
  });
});

test("examples come from the API and health is reported", async () => {
  await withServer(scriptedRunner(makeCompleteClient), async (base) => {
    const examples = await (await fetch(`${base}/api/examples`)).json() as { id: string; request: string }[];
    assert.deepEqual(examples.map((item) => item.id), ["incomplete", "complete"]);
    assert.equal((await (await fetch(`${base}/api/health`)).json() as { status: string }).status, "ok");
  });
});

test("real demo-inputs files back the examples", async () => {
  const { loadExamples } = await import("./examples.js");
  const examples = await loadExamples();
  const incomplete = JSON.parse(await readFile(new URL("../../../demo-inputs/incomplete.json", import.meta.url), "utf8"));
  assert.equal(examples.find((item) => item.id === "incomplete")?.request, incomplete.request);
});

test("incomplete run reports clarification, creates no tickets and passes demo validation", async () => {
  await withServer(scriptedRunner(() => incompleteClient), async (base) => {
    const { runId } = await (await post(`${base}/api/runs`, { request: "Build an AI application for claims." })).json() as { runId: string };
    const state = await finished(base, runId);
    assert.equal(state.status, "completed");
    assert.equal(state.result?.status, "needs_information");
    assert.equal(state.result?.tickets.length, 0);
    assert.equal(state.result?.questions[0]?.topic, "users");
    assert.equal(state.validation?.overall, "PASS");
    assert.deepEqual(await (await fetch(`${base}/api/backlog`)).json(), []);
    assert.ok(state.events.some((event) => event.toolName === "request_missing_information" && event.status === "completed"));
    assert.ok(!state.events.some((event) => event.phase === "tool"));
  });
});

test("complete run exposes real events, hierarchy and passing validation", async () => {
  await withServer(scriptedRunner(makeCompleteClient), async (base) => {
    const { runId } = await (await post(`${base}/api/runs`, { request: "Complete synthetic request" })).json() as { runId: string };
    const state = await finished(base, runId);
    assert.equal(state.result?.status, "backlog_created");
    assert.equal(state.validation?.overall, "PASS");
    const backlog = await (await fetch(`${base}/api/backlog`)).json() as { id: string }[];
    assert.deepEqual(state.result?.tickets.map((item) => item.id), backlog.map((item) => item.id));
    assert.ok(state.result?.tickets.every((item) => item.state === "created"));
    const toolEvents = state.events.filter((event) => event.phase === "tool");
    assert.equal(toolEvents.length, 7);
    assert.deepEqual(toolEvents.map((event) => event.ticketId), backlog.map((item) => item.id));
    assert.ok(toolEvents.every((event) => event.status === "completed" && typeof event.durationMs === "number"));
    assert.deepEqual(state.events.map((event) => event.phase).filter((p) => p !== "tool"),
      ["assessment", "planning", "review", "report"]);

    const second = await (await post(`${base}/api/runs`, { request: "Complete synthetic request" })).json() as { runId: string };
    const rerun = await finished(base, second.runId);
    assert.ok(rerun.events.filter((event) => event.phase === "tool").every((event) => event.status === "reused"));
    assert.ok(rerun.result?.tickets.every((item) => item.state === "reused"));
  });
});

test("a second run is rejected while one is active, and reset is blocked then allowed", async () => {
  let release!: () => void;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  const slow: Runner = async (request, store, recorder) => {
    await gate;
    return scriptedRunner(makeCompleteClient)(request, store, recorder);
  };
  await withServer(slow, async (base) => {
    const { runId } = await (await post(`${base}/api/runs`, { request: "First" })).json() as { runId: string };
    assert.equal((await post(`${base}/api/runs`, { request: "Second" })).status, 409);
    assert.equal((await post(`${base}/api/reset`, {})).status, 409);
    release();
    await finished(base, runId);
    assert.equal((await post(`${base}/api/reset`, {})).status, 200);
    assert.deepEqual(await (await fetch(`${base}/api/backlog`)).json(), []);
  });
});

test("failed runs surface an error and reset deletes only the configured store", async () => {
  const failing: Runner = async () => { throw new Error("Copilot unavailable"); };
  await withServer(failing, async (base, dir) => {
    const { runId } = await (await post(`${base}/api/runs`, { request: "Request" })).json() as { runId: string };
    const state = await finished(base, runId);
    assert.equal(state.status, "failed");
    assert.match(state.error ?? "", /Copilot unavailable/);
    await writeFile(join(dir, "other.json"), "{}");
    assert.equal((await post(`${base}/api/reset`, {})).status, 200);
    assert.equal(await readFile(join(dir, "other.json"), "utf8"), "{}");
  });
});

test("freshStart clears the demo store before a run, so nothing is reused", async () => {
  await withServer(scriptedRunner(makeCompleteClient), async (base) => {
    const first = await (await post(`${base}/api/runs`, { request: "Complete synthetic request" })).json() as { runId: string };
    await finished(base, first.runId);
    const second = await (await post(`${base}/api/runs`, { request: "Complete synthetic request", freshStart: true })).json() as { runId: string };
    const state = await finished(base, second.runId);
    assert.ok(state.result?.tickets.every((item) => item.state === "created"));
    assert.equal(state.result?.tickets[0]?.id, "DEMO-100");
  });
});

test("store paths outside the demo-data directory are rejected", () => {
  const dir = join(tmpdir(), "demo-data");
  assert.throws(() => resolveDemoStorePath(join(dir, "..", "tickets.json"), dir), /inside/);
  assert.throws(() => resolveDemoStorePath(dir, dir), /inside/);
  assert.throws(() => resolveDemoStorePath(join(dir, "notes.txt"), dir), /inside/);
  assert.equal(resolveDemoStorePath(join(dir, "tickets.json"), dir), join(dir, "tickets.json"));
  assert.throws(() => createDemoServer({ storePath: join(dir, "..", "x.json"), dataDir: dir, runner: async () => { throw new Error(); } }));
});

test("static routes serve only the fixed UI assets", async () => {
  await withServer(scriptedRunner(makeCompleteClient), async (base) => {
    const html = await (await fetch(`${base}/`)).text();
    assert.match(html, /Agentic SDLC Backlog Builder/);
    assert.match(html, /Local proof of concept – synthetic data only/);
    assert.equal((await fetch(`${base}/assets/..%2Fpackage.json`)).status, 404);
    assert.equal((await fetch(`${base}/package.json`)).status, 404);
    assert.equal((await fetch(`${base}/assets/../../package.json`)).status, 404);
  });
});

test("validation fails for untraceable IDs, duplicates and missing categories", () => {
  const base = {
    status: "backlog_created" as const, questions: [],
    calls: [{ name: "create_feature", success: true, id: "DEMO-100", created: true }],
    tickets: [{ ...feature, id: "DEMO-100", type: "Feature" as const }, { ...feature, id: "DEMO-100", type: "Feature" as const },
      { ...story("DEMO-100"), id: "DEMO-999", type: "User Story" as const }],
  };
  const report = validateRun(base);
  const failed = report.checks.filter((check) => check.status === "FAIL").map((check) => check.id);
  assert.equal(report.overall, "FAIL");
  for (const id of ["story", "categories", "hierarchy", "unique-ids", "traceable"]) assert.ok(failed.includes(id), id);
  const clarificationWithTicket = validateRun({
    status: "needs_information", questions: questions.questions, tickets: [],
    calls: [{ name: "create_feature", success: true, id: "DEMO-100", created: true }],
  });
  assert.equal(clarificationWithTicket.overall, "FAIL");
});
