SELECT
received_at,
client_install_id,
speaker,
text,
raw_input,
unknown_words_json
FROM dialogue_logs
WHERE unknown_words_json IS NOT NULL
AND unknown_words_json != ''
AND unknown_words_json != '[]'
ORDER BY received_at DESC
LIMIT 200;
