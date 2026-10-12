import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { randomUUID } from "node:crypto";
import { readFile, stat, unlink } from "node:fs/promises";
import { isAbsolute, relative, resolve } from "node:path";
import { z } from "zod";
import type { AgentResult } from "../agent/run-agent.js";
import { JsonTicketStore } from "../storage/json-ticket-store.js";
import type { TicketStore } from "../storage/tickets.js";
import { RunEventLog, type RunRecorder } from "../telemetry/events.js";
import type { BacklogItem, Example, RunResult, RunState } from "./api-types.js";
import { loadExamples } from "./examples.js";
import { validateRun } from "./validation.js";

export type Runner = (request: string, store: TicketStore, recorder: RunRecorder) => Promise<AgentResult>;

export interface DemoServerOptions {
  storePath: string;
  dataDir: string;
  runner: Runner;
  examples?: () => Promise<Example[]>;
  sourceWebDir?: URL;
  compiledWebDir?: URL;
}

const runBody = z.object({
  request: z.string().trim().min(1, "A software requirement is required").max(8000),
  freshStart: z.boolean().optional(),
}).strict();
const MAX_BODY = 64 * 1024;
const MAX_RUNS = 20;
const SOURCE_ASSETS: Record<string, string> = {
  "/": "index.html", "/index.html": "index.html", "/styles.css": "styles.css",
};
const TYPES: Record<string, string> = {
  ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8",
};

class HttpError extends Error {
  constructor(readonly status: number, message: string) { super(message); }
}

export function resolveDemoStorePath(storePath: string, dataDir: string): string {
  const root = resolve(dataDir);
  const target = resolve(storePath);
  const path = relative(root, target);
  if (!path || path.startsWith("..") || isAbsolute(path) || !target.endsWith(".json")) {
    throw new Error(`Demo store must be a .json file inside ${root}`);
  }
  return target;
}

export function createDemoServer(options: DemoServerOptions): Server {
  const storePath = resolveDemoStorePath(options.storePath, options.dataDir);
  const store = new JsonTicketStore(storePath);
  const sourceDir = options.sourceWebDir ?? new URL("../../../src/web/", import.meta.url);
  const compiledDir = options.compiledWebDir ?? new URL("../../web/", import.meta.url);
  const examples = options.examples ?? (() => loadExamples());
  const runs = new Map<string, RunState>();
  let active: string | undefined;

  const send = (res: ServerResponse, status: number, body: unknown) => {
    res.writeHead(status, { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" });
    res.end(JSON.stringify(body));
  };

  async function readJson(req: IncomingMessage): Promise<unknown> {
    if (!req.headers["content-type"]?.toLowerCase().startsWith("application/json")) {
      throw new HttpError(415, "Content-Type must be application/json");
    }
    const chunks: Buffer[] = [];
    let size = 0;
    for await (const chunk of req) {
      size += (chunk as Buffer).length;
      if (size > MAX_BODY) throw new HttpError(413, "Request body too large");
      chunks.push(chunk as Buffer);
    }
    try {
      return JSON.parse(Buffer.concat(chunks).toString("utf8"));
    } catch {
      throw new HttpError(400, "Invalid JSON body");
    }
  }

  function startRun(request: string): RunState {
    const log = new RunEventLog();
    const state: RunState = { runId: randomUUID(), status: "running", startedAt: new Date().toISOString(), events: [] };
    const sync = () => { state.events = log.events(); };
    const recorder: RunRecorder = {
      begin: (event) => { const id = log.begin(event); sync(); return id; },
      finish: (id, patch) => { log.finish(id, patch); sync(); },
    };
    runs.set(state.runId, state);
    for (const id of [...runs.keys()].slice(0, Math.max(0, runs.size - MAX_RUNS))) runs.delete(id);
    active = state.runId;
    void options.runner(request, store, recorder).then((agent) => {
      const status = new Map<string, boolean>();
      for (const call of agent.calls) {
        if (call.success && call.id) status.set(call.id, status.get(call.id) || call.created === true);
      }
      const result: RunResult = {
        status: agent.status, questions: agent.questions, summary: agent.summary,
        tickets: agent.tickets.map((ticket): BacklogItem =>
          ({ ...ticket, state: status.get(ticket.id) ? "created" : "reused" })),
      };
      state.result = result;
      state.validation = validateRun(agent);
      state.status = "completed";
    }, (error: unknown) => {
      state.error = error instanceof Error ? error.message : String(error);
      state.status = "failed";
    }).finally(() => {
      sync();
      state.finishedAt = new Date().toISOString();
      active = undefined;
    });
    return state;
  }

  async function serveFile(res: ServerResponse, url: URL, name: string) {
    const extension = name.slice(name.lastIndexOf("."));
    try {
      const body = await readFile(url);
      res.writeHead(200, { "content-type": TYPES[extension] ?? "application/octet-stream", "cache-control": "no-store" });
      res.end(body);
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === "ENOENT") {
        throw new HttpError(404, "Not found (run npm run build:web if the UI script is missing)");
      }
      throw error;
    }
  }

  async function resetStore() {
    if (active) throw new HttpError(409, "A demo run is active; wait for it to finish before resetting");
    const target = resolveDemoStorePath(storePath, options.dataDir);
    if (await stat(`${target}.lock`).then(() => true, () => false)) {
      throw new HttpError(409, "The demo store is locked by another writer");
    }
    await unlink(target).catch((error: NodeJS.ErrnoException) => { if (error.code !== "ENOENT") throw error; });
  }

  async function handle(req: IncomingMessage, res: ServerResponse) {
    const url = new URL(req.url ?? "/", "http://localhost");
    const path = url.pathname;
    const method = req.method ?? "GET";
    if (path.startsWith("/api/")) {
      const runMatch = /^\/api\/runs\/([0-9a-f-]{36})$/.exec(path);
      if (path === "/api/health" && method === "GET") return send(res, 200, { status: "ok", running: Boolean(active) });
      if (path === "/api/examples" && method === "GET") return send(res, 200, await examples());
      if (path === "/api/backlog" && method === "GET") {
        const items = await store.list();
        return send(res, 200, items.map((item): BacklogItem => ({ ...item, state: "existing" })));
      }
      if (runMatch && method === "GET") {
        const state = runs.get(runMatch[1]!);
        if (!state) throw new HttpError(404, "Run not found");
        return send(res, 200, state);
      }
      if (path === "/api/runs" && method === "POST") {
        const parsed = runBody.safeParse(await readJson(req));
        if (!parsed.success) throw new HttpError(400, parsed.error.issues.map((issue) => issue.message).join("; "));
        if (active) throw new HttpError(409, "Another demo run is already active");
        if (parsed.data.freshStart) await resetStore();
        return send(res, 202, { runId: startRun(parsed.data.request).runId });
      }
      if (path === "/api/reset" && method === "POST") {
        await resetStore();
        runs.clear();
        return send(res, 200, { status: "reset" });
      }
      throw new HttpError(path in { "/api/health": 1, "/api/examples": 1, "/api/backlog": 1, "/api/runs": 1, "/api/reset": 1 } || runMatch ? 405 : 404,
        "Unsupported API route");
    }
    if (method !== "GET") throw new HttpError(405, "Method not allowed");
    const asset = SOURCE_ASSETS[path];
    if (asset) return serveFile(res, new URL(asset, sourceDir), asset);
    const script = /^\/assets\/((?:[a-z0-9-]+\/)*[a-z0-9-]+\.js)$/.exec(path);
    if (script) return serveFile(res, new URL(script[1]!, compiledDir), script[1]!);
    throw new HttpError(404, "Not found");
  }

  return createServer((req, res) => {
    res.setHeader("content-security-policy", "default-src 'self'; style-src 'self'; script-src 'self'; frame-ancestors 'none'");
    res.setHeader("x-content-type-options", "nosniff");
    handle(req, res).catch((error: unknown) => {
      const status = error instanceof HttpError ? error.status : 500;
      if (res.headersSent) return void res.end();
      send(res, status, { error: error instanceof Error ? error.message : "Internal error" });
    });
  });
}
