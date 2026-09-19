SELECT
client_install_id,
COUNT(*) AS log_count,
MIN(received_at) AS first_received_at,
MAX(received_at) AS last_received_at
FROM dialogue_logs
GROUP BY client_install_id
ORDER BY log_count DESC;
