import { resolve } from "node:path";
import { runCopilot } from "../agent/run-agent.js";
import { createDemoServer } from "./app.js";

const dataDir = resolve("data");
const storePath = resolve(process.env.DEMO_STORE_PATH ?? "data/web-demo-tickets.json");
const port = Number(process.env.PORT ?? 4173);
const host = "127.0.0.1";

const server = createDemoServer({
  storePath, dataDir,
  runner: (request, store, recorder) => runCopilot(request, store, process.env.COPILOT_MODEL, recorder),
});
server.listen(port, host, () => {
  console.log(`Agentic SDLC Backlog Builder (local proof of concept, synthetic data only)`);
  console.log(`Open http://${host}:${port}\nDemo store: ${storePath}`);
});
