export interface Env {
  DB: D1Database;
  NEKOLPOS_UPLOAD_KEY?: string;
  REQUIRE_UPLOAD_KEY?: string;
}

const ENDPOINT_PATH = "/api/dialogue-logs";
const SCHEMA_VERSION = "nekolpos-dialogue-log/v1";
const MAX_PAYLOAD_BYTES = 256 * 1024;
const MAX_LOGS_PER_REQUEST = 100;
const MAX_TEXT_CHARS = 10_000;

type JsonObject = Record<string, unknown>;

type DialogueLogPayload = {
  schema_version?: unknown;
  logs?: unknown;
};

class HttpError extends Error {
  public readonly status: number;

  public constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const requestId = getRequestId(request);

    try {
      const url = new URL(request.url);

      if (url.pathname !== ENDPOINT_PATH) {
        return withCors(new Response("Not Found", { status: 404 }));
      }

      if (request.method === "OPTIONS") {
        return handleOptions();
      }

      if (request.method !== "POST") {
        return withCors(new Response("Method Not Allowed", {
          status: 405,
          headers: { Allow: "POST, OPTIONS" }
        }));
      }

      requireJsonContentType(request);
      requireAuthorized(request, env);

      const rawBody = await readBoundedBody(request);
      const payload = parsePayload(rawBody);
      const logs = validatePayload(payload);
      const receivedAt = new Date().toISOString();
      const savedCount = await insertLogs(env.DB, logs, receivedAt, String(payload.schema_version));
      const duplicateCount = logs.length - savedCount;

      ctx.waitUntil(logInfo("dialogue_logs_ingested", {
        requestId,
        receivedCount: logs.length,
        savedCount,
        duplicateCount
      }));

      return withCors(new Response(null, { status: 204 }));
    } catch (error) {
      const status = error instanceof HttpError ? error.status : 500;
      const reason = error instanceof Error ? error.message : "internal_error";

      ctx.waitUntil(logInfo("dialogue_logs_error", {
        requestId,
        status,
        reason
      }));

      return withCors(new Response(status === 500 ? "Internal Server Error" : reason, { status }));
    }
  }
};

function handleOptions(): Response {
  return withCors(new Response(null, {
    status: 204,
    headers: {
      Allow: "POST, OPTIONS",
      "Access-Control-Max-Age": "86400"
    }
  }));
}

function withCors(response: Response): Response {
  const headers = new Headers(response.headers);
  headers.set("Access-Control-Allow-Origin", "*");
  headers.set("Access-Control-Allow-Methods", "POST, OPTIONS");
  headers.set("Access-Control-Allow-Headers", "Content-Type, X-Nekolpos-Upload-Key");
  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers
  });
}

function requireJsonContentType(request: Request): void {
  const contentType = request.headers.get("content-type") ?? "";
  const mediaType = contentType.split(";")[0]?.trim().toLowerCase();
  if (mediaType !== "application/json" && !mediaType.endsWith("+json")) {
    throw new HttpError(415, "unsupported_media_type");
  }
}

function requireAuthorized(request: Request, env: Env): void {
  if (!isAuthRequired(env)) {
    return;
  }

  const expectedKey = env.NEKOLPOS_UPLOAD_KEY;
  const suppliedKey = request.headers.get("x-nekolpos-upload-key");
  if (!expectedKey || !suppliedKey || suppliedKey !== expectedKey) {
    throw new HttpError(401, "unauthorized");
  }
}

function isAuthRequired(env: Env): boolean {
  return String(env.REQUIRE_UPLOAD_KEY ?? "false").toLowerCase() === "true";
}

async function readBoundedBody(request: Request): Promise<string> {
  const contentLength = request.headers.get("content-length");
  if (contentLength !== null) {
    const length = Number(contentLength);
    if (!Number.isFinite(length) || length > MAX_PAYLOAD_BYTES) {
      throw new HttpError(413, "payload_too_large");
    }
  }

  const rawBody = await request.text();
  if (new TextEncoder().encode(rawBody).byteLength > MAX_PAYLOAD_BYTES) {
    throw new HttpError(413, "payload_too_large");
  }

  return rawBody;
}

function parsePayload(rawBody: string): DialogueLogPayload {
  try {
    const parsed = JSON.parse(rawBody) as unknown;
    if (!isObject(parsed)) {
      throw new HttpError(400, "payload_must_be_object");
    }

    return parsed as DialogueLogPayload;
  } catch (error) {
    if (error instanceof HttpError) {
      throw error;
    }

    throw new HttpError(400, "invalid_json");
  }
}

function validatePayload(payload: DialogueLogPayload): JsonObject[] {
  if (payload.schema_version !== SCHEMA_VERSION) {
    throw new HttpError(400, "invalid_schema_version");
  }

  if (!Array.isArray(payload.logs)) {
    throw new HttpError(400, "logs_must_be_array");
  }

  if (payload.logs.length < 1 || payload.logs.length > MAX_LOGS_PER_REQUEST) {
    throw new HttpError(400, "invalid_logs_length");
  }

  return payload.logs.map((entry, index) => {
    if (!isObject(entry)) {
      throw new HttpError(400, `logs_${index}_must_be_object`);
    }

    const logId = getString(entry, "log_id", "logId", "LogId");
    if (!logId || logId.trim().length === 0) {
      throw new HttpError(400, `logs_${index}_missing_log_id`);
    }

    const text = getString(entry, "text", "Text");
    if (text !== null && text.length > MAX_TEXT_CHARS) {
      throw new HttpError(400, `logs_${index}_text_too_long`);
    }

    return entry;
  });
}

async function insertLogs(
  db: D1Database,
  logs: JsonObject[],
  receivedAt: string,
  schemaVersion: string
): Promise<number> {
  const statements = logs.map((log) => {
    const logId = requireString(log, "log_id", "logId", "LogId");
    const payloadJson = JSON.stringify(log);

    return db.prepare(
      `INSERT OR IGNORE INTO dialogue_logs (
        log_id,
        timestamp,
        received_at,
        schema_version,
        language,
        speaker,
        speaker_display_name,
        source,
        text,
        payload_json
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
    ).bind(
      logId,
      getString(log, "timestamp", "Timestamp"),
      receivedAt,
      schemaVersion,
      getString(log, "language", "Language"),
      getString(log, "speaker", "Speaker"),
      getString(log, "speaker_display_name", "speakerDisplayName", "SpeakerDisplayName"),
      getString(log, "source", "Source"),
      getString(log, "text", "Text"),
      payloadJson
    );
  });

  const results = await db.batch(statements);
  return results.reduce((count, result) => {
    const meta = result.meta as { changes?: number };
    return count + Number(meta.changes ?? 0);
  }, 0);
}

function isObject(value: unknown): value is JsonObject {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function getString(source: JsonObject, ...keys: string[]): string | null {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === "string") {
      return value;
    }
  }

  return null;
}

function requireString(source: JsonObject, ...keys: string[]): string {
  const value = getString(source, ...keys);
  if (value === null) {
    throw new HttpError(400, "missing_required_string");
  }

  return value;
}

function getRequestId(request: Request): string {
  return request.headers.get("cf-ray") ?? crypto.randomUUID();
}

async function logInfo(message: string, data: Record<string, unknown>): Promise<void> {
  console.log(message, data);
}
