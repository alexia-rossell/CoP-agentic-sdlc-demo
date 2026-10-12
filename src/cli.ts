import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { z } from "zod";
import { runCopilot } from "./agent/run-agent.js";
import { JsonTicketStore } from "./storage/json-ticket-store.js";

async function main() {
  const [scenario = "complete", ...rest] = process.argv.slice(2);
  if (scenario === "--help") {
    console.log('Usage: npm run demo -- [complete|incomplete]\n       npm run demo -- --request "Software requirement"');
    return;
  }
  let request: string;
  if (scenario === "--request" && rest.length === 1) {
    request = rest[0]!;
  } else if (["complete", "incomplete"].includes(scenario) && !rest.length) {
    const input = z.object({ request: z.string().min(1) }).parse(JSON.parse(
      await readFile(new URL(`../../demo-inputs/${scenario}.json`, import.meta.url), "utf8"),
    ));
    request = input.request;
  } else {
    throw new Error('Use complete, incomplete, or --request "Software requirement"');
  }
  const path = resolve(process.env.DEMO_STORE_PATH ?? "data/tickets.json");
  const result = await runCopilot(request, new JsonTicketStore(path), process.env.COPILOT_MODEL);
  console.log(result.summary);
  if (result.tickets.length) console.log(`\nLocal store: ${path}`);
}

main().catch((error: unknown) => {
  console.error(`Demo failed: ${error instanceof Error ? error.message : String(error)}`);
  console.error("Check Copilot login/runtime availability. Successful earlier writes may remain in the local store.");
  process.exitCode = 1;
});
