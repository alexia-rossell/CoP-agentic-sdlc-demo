import assert from "node:assert/strict";
import { test } from "node:test";
import { mkdtemp, open, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { type SessionConfig, type Tool } from "@github/copilot-sdk";
import { runAgent, type AgentClient, type AgentSession } from "../src/agent/run-agent.js";
import { JsonTicketStore } from "../src/storage/json-ticket-store.js";
import { type FeatureInput, type StoryInput, type TaskInput, type TicketStore } from "../src/storage/tickets.js";
import { createBacklogTools } from "../src/tools/backlog-tools.js";
import { casesSchema, evaluateResult } from "./expectations.js";

const feature: FeatureInput = {
  title: "Claims document processing", description: "Employee claims workflow",
  businessValue: "Reduce manual entry while preserving review", tags: ["demo", "claims"],
};
const story = (parentId: string): StoryInput => ({
  parentId, title: "Review and submit a claim", persona: "Employee",
  goal: "Correct extracted fields", benefit: "Submit accurate information",
  acceptanceCriteria: ["Authenticated employees can review, correct and submit fields; changes are audited"],
});
const task = (parentId: string, category: TaskInput["category"] = "implementation"): TaskInput => ({
  parentId, title: `Claims ${category} work`, description: `Implement and verify ${category} behavior`,
  category, priority: "high",
});
const clarification = {
  questions: [{ topic: "workflow", question: "Who uses the application and what should it do?", reason: "A backlog needs a user and workflow" }],
};

async function withStore(run: (store: JsonTicketStore, path: string) => Promise<void>) {
  const directory = await mkdtemp(join(tmpdir(), "agentic-sdlc-test-"));
  const path = join(directory, "tickets.json");
  try {
    await run(new JsonTicketStore(path), path);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

async function invoke(tools: Tool[], name: string, args: unknown): Promise<unknown> {
  const tool = tools.find((candidate) => candidate.name === name);
  assert.ok(tool?.handler, `Registered handler for ${name}`);
  return tool.handler(args, { sessionId: "test", toolCallId: "test", toolName: name, arguments: args });
}

test("store persists sequential IDs and enforces Feature → Story → Task parents", async () => {
  await withStore(async (store, path) => {
    const first = await store.create({ ...feature, type: "Feature" });
    const second = await store.create({ ...story(first.item.id), type: "User Story" });
    const third = await store.create({ ...task(second.item.id), type: "Task" });
    assert.deepEqual([first.item.id, second.item.id, third.item.id], ["DEMO-100", "DEMO-101", "DEMO-102"]);
    const reopened = new JsonTicketStore(path);
    assert.deepEqual(await reopened.list(), [first.item, second.item, third.item]);
    await assert.rejects(store.create({ ...task(first.item.id), type: "Task" }), /User Story parent/);
    await assert.rejects(store.create({ ...story("DEMO-999"), type: "User Story" }), /Feature parent/);
    assert.equal((await store.list()).length, 3);
  });
});

test("duplicate titles are reused across concurrent calls and restarts without consuming IDs", async () => {
  await withStore(async (store, path) => {
    const results = await Promise.all([
      store.create({ ...feature, type: "Feature" }),
      store.create({ ...feature, title: "  CLAIMS   document processing ", type: "Feature" }),
    ]);
    assert.deepEqual(results.map((result) => result.created), [true, false]);
    assert.equal(results[0]!.item.id, results[1]!.item.id);
    const reopened = new JsonTicketStore(path);
    assert.equal((await reopened.create({ ...feature, type: "Feature" })).created, false);
    const child = await reopened.create({ ...story("DEMO-100"), type: "User Story" });
    assert.equal(child.item.id, "DEMO-101");
    const nextFeature = await reopened.create({ ...feature, title: "Another feature", type: "Feature" });
    const otherChild = await reopened.create({ ...story(nextFeature.item.id), type: "User Story" });
    assert.equal(otherChild.created, true);
  });
});

test("store fails closed for corrupt data and competing writers", async () => {
  await withStore(async (store, path) => {
    await writeFile(path, "not JSON");
    await assert.rejects(store.create({ ...feature, type: "Feature" }));
    assert.equal(await readFile(path, "utf8"), "not JSON");
    await rm(path);
    const lock = await open(`${path}.lock`, "wx");
    try {
      await assert.rejects(store.create({ ...feature, type: "Feature" }), { code: "EEXIST" });
      assert.equal((await store.list()).length, 0);
    } finally {
      await lock.close();
      await rm(`${path}.lock`);
    }
    assert.equal((await store.create({ ...feature, type: "Feature" })).item.id, "DEMO-100");
  });
});

test("clarification prevents writes, including enabling writes after questions", async () => {
  await withStore(async (store) => {
    const tools = createBacklogTools(store);
    await assert.rejects(invoke(tools.creation, "create_feature", feature), /complete assessment/);
    await invoke([tools.clarification], "request_missing_information", clarification);
    assert.throws(() => tools.enableWrites(), /clarification/);
    await assert.rejects(invoke(tools.creation, "create_feature", feature));
    assert.equal((await store.list()).length, 0);
    assert.equal(tools.questions.length, 1);
  });
});

test("tools reject invalid inputs and never report IDs for failed persistence", async () => {
  const failing: TicketStore = {
    list: async () => [],
    create: async () => { throw new Error("Disk unavailable"); },
  };
  const tools = createBacklogTools(failing);
  tools.enableWrites();
  await assert.rejects(invoke(tools.creation, "create_feature", { ...feature, title: "" }));
  await assert.rejects(invoke(tools.creation, "create_user_story", { ...story("DEMO-100"), acceptanceCriteria: [] }));
  await assert.rejects(invoke(tools.creation, "create_feature", feature), /Disk unavailable/);
  assert.equal(tools.tickets.size, 0);
  assert.ok(tools.calls.every((call) => !call.success && !call.id));
});

test("existing parents must be resolved successfully through tools before child writes", async () => {
  await withStore(async (store) => {
    await store.create({ ...feature, type: "Feature" });
    await store.create({ ...story("DEMO-100"), type: "User Story" });
    const tools = createBacklogTools(store);
    tools.enableWrites();
    await assert.rejects(invoke(tools.creation, "create_engineering_task", task("DEMO-101")), /Resolve the User Story/);
    await assert.rejects(invoke(tools.creation, "create_user_story", story("DEMO-100")), /Resolve the Feature/);
    assert.equal((await store.list()).length, 2);
    await invoke(tools.creation, "create_feature", feature);
    await invoke(tools.creation, "create_user_story", story("DEMO-100"));
    const result = await invoke(tools.creation, "create_engineering_task", task("DEMO-101")) as { id: string };
    assert.equal(result.id, "DEMO-102");
    assert.equal(tools.tickets.size, 3);
  });
});

function fakeSession(send: (...args: unknown[]) => Promise<unknown>): AgentSession {
  return {
    sendAndWait: send as AgentSession["sendAndWait"],
    disconnect: async () => {},
    abort: async () => {},
  };
}

test("incomplete orchestration selects clarification only and leaves an existing backlog untouched", async () => {
  await withStore(async (store) => {
    await store.create({ ...feature, type: "Feature" });
    let sessions = 0;
    const client: AgentClient = {
      createSession: async (config) => {
        sessions++;
        assert.deepEqual(config.availableTools, ["custom:request_missing_information"]);
        return fakeSession(async () => {
          await invoke(config.tools!, "request_missing_information", clarification);
          return { ready: false, reason: "Missing workflow" };
        });
      },
    };
    const result = await runAgent("Build an AI application for claims.", store, client);
    assert.equal(result.status, "needs_information");
    assert.equal(result.tickets.length, 0);
    assert.equal(sessions, 1);
    assert.equal((await store.list()).length, 1);
  });
});

test("complete orchestration creates hierarchy, invokes review and ignores fabricated assistant IDs", async () => {
  await withStore(async (store) => {
    let sessions = 0;
    let creationTurns = 0;
    let active: SessionConfig;
    const client: AgentClient = {
      createSession: async (config) => {
        sessions++;
        active = config;
        if (sessions === 1) return fakeSession(async () => ({ ready: true, reason: "Complete workflow" }));
        assert.ok(Array.isArray(config.availableTools));
        assert.ok(!config.availableTools.includes("custom:request_missing_information"));
        let parent = "";
        return fakeSession(async () => {
          creationTurns++;
          if (creationTurns === 1) {
            const f = await invoke(active.tools!, "create_feature", feature) as { id: string };
            const s = await invoke(active.tools!, "create_user_story", story(f.id)) as { id: string };
            parent = s.id;
            await invoke(active.tools!, "create_engineering_task", task(parent));
          } else {
            for (const category of ["security", "testing", "observability", "operations"] as const) {
              await invoke(active.tools!, "create_engineering_task", task(parent, category));
            }
          }
          return { data: { content: "Created DEMO-999 (fabricated)" } };
        });
      },
    };
    const result = await runAgent("Complete employee claims workflow", store, client);
    assert.equal(sessions, 2);
    assert.equal(creationTurns, 2);
    assert.equal(result.tickets.length, 7);
    assert.match(result.summary, /DEMO-100 Feature/);
    assert.ok(!result.summary.includes("DEMO-999"));
    const cases = casesSchema.parse(JSON.parse(await readFile(
      new URL("../../evaluations/cases.json", import.meta.url), "utf8",
    )));
    assert.deepEqual(evaluateResult(cases[1]!, result), []);
    assert.ok(evaluateResult(cases[0]!, result).length > 0);
  });
});

test("assessment fails closed when agent does not call the required clarification tool", async () => {
  await withStore(async (store) => {
    const client: AgentClient = {
      createSession: async () => fakeSession(async () => ({ ready: false, reason: "Missing workflow" })),
    };
    await assert.rejects(runAgent("Incomplete", store, client), /clarification questions/);
    assert.equal((await store.list()).length, 0);
  });
});

test("review cannot report success with only implementation work", async () => {
  await withStore(async (store) => {
    let sessions = 0;
    let turns = 0;
    const client: AgentClient = {
      createSession: async (config) => {
        if (++sessions === 1) return fakeSession(async () => ({ ready: true, reason: "Complete workflow" }));
        return fakeSession(async () => {
          if (++turns === 1) {
            const f = await invoke(config.tools!, "create_feature", feature) as { id: string };
            const s = await invoke(config.tools!, "create_user_story", story(f.id)) as { id: string };
            await invoke(config.tools!, "create_engineering_task", task(s.id));
          }
          return { data: { content: "Review complete" } };
        });
      },
    };
    await assert.rejects(runAgent("Complete workflow", store, client), /missing required work: security, testing, observability, operations/);
    assert.equal((await store.list()).length, 3);
  });
});

test("timeout aborts the agent before disconnecting and cannot report a fabricated backlog", async () => {
  await withStore(async (store) => {
    const events: string[] = [];
    const client: AgentClient = {
      createSession: async () => ({
        ...fakeSession(async () => { throw new Error("Timed out"); }),
        abort: async () => { events.push("abort"); },
        disconnect: async () => { events.push("disconnect"); },
      }),
    };
    await assert.rejects(runAgent("Request", store, client), /Timed out/);
    assert.deepEqual(events, ["abort", "disconnect"]);
    assert.equal((await store.list()).length, 0);
  });
});
