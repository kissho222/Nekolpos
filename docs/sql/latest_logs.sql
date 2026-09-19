SELECT
received_at,
client_install_id,
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
ORDER BY received_at DESC
LIMIT 100;
