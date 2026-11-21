// jebaited_player diagnostics endpoint (cycle 6, N1). NOT deployed: the owner deploys it to their own free
// Cloudflare account when they decide to, then puts the https URL into ErrorReporting.Endpoint.
// Stores only the redacted JSON report the player sends. No IP address, no headers, no cookies are stored.
// Bindings: KV namespace REPORTS. Limits: 16 KB per report, 200 reports per day (global, simple counter).
export default {
  async fetch(request, env) {
    if (request.method !== "POST") return new Response("Method not allowed", { status: 405 });
    const text = await request.text();
    if (text.length > 16384) return new Response("Too large", { status: 413 });
    let report;
    try { report = JSON.parse(text); } catch { return new Response("Bad JSON", { status: 400 }); }
    const required = ["Id", "CreatedUtc", "AppVersion", "Level", "Type", "Message"];
    if (!required.every(k => typeof report[k] === "string")) return new Response("Bad report", { status: 400 });
    const day = new Date().toISOString().slice(0, 10);
    const counterKey = "count:" + day;
    const count = parseInt((await env.REPORTS.get(counterKey)) ?? "0", 10);
    if (count >= 200) return new Response("Daily limit", { status: 429 });
    await env.REPORTS.put(counterKey, String(count + 1), { expirationTtl: 172800 });
    const clean = Object.fromEntries(required.concat(["Edition", "Os", "Category", "Component", "Exception"])
      .map(k => [k, typeof report[k] === "string" ? report[k].slice(0, 4000) : null]));
    await env.REPORTS.put(`report:${day}:${clean.Id}`, JSON.stringify(clean), { expirationTtl: 60 * 60 * 24 * 90 });
    return new Response("OK", { status: 200 });
  },
};
