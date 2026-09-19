CREATE TABLE IF NOT EXISTS dialogue_logs (
  log_id TEXT PRIMARY KEY,
  timestamp TEXT,
  received_at TEXT NOT NULL,
  schema_version TEXT NOT NULL,
  language TEXT,
  speaker TEXT,
  speaker_display_name TEXT,
  source TEXT,
  text TEXT,
  payload_json TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_dialogue_logs_timestamp
ON dialogue_logs(timestamp);

CREATE INDEX IF NOT EXISTS idx_dialogue_logs_received_at
ON dialogue_logs(received_at);

CREATE INDEX IF NOT EXISTS idx_dialogue_logs_speaker
ON dialogue_logs(speaker);

CREATE INDEX IF NOT EXISTS idx_dialogue_logs_source
ON dialogue_logs(source);
