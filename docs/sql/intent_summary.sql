SELECT
selected_intent,
COUNT(*) AS count
FROM dialogue_logs
WHERE selected_intent IS NOT NULL
AND selected_intent != ''
GROUP BY selected_intent
ORDER BY count DESC;
