SELECT
received_at,
timestamp,
client_session_id,
upload_batch_id,
platform,
host,
speaker,
speaker_display_name,
source,
text,
raw_input,
selected_intent,
selected_response_id
FROM dialogue_logs
WHERE client_install_id = ?
ORDER BY received_at ASC;
