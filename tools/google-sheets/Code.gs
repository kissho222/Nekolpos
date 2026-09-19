const DEFAULT_API_BASE_URL = 'https://nekolpos-log-ingest.nekolpos.workers.dev';
const ADMIN_KEY_PROPERTY = 'NEKOLPOS_ADMIN_KEY';

const SETTINGS_SHEET = 'Settings';
const API_BASE_URL_KEY = 'API_BASE_URL';
const LAST_SYNC_KEY = 'LAST_SYNC_RECEIVED_AT';
const LAST_SYNC_LOG_ID_KEY = 'LAST_SYNC_LOG_ID';
const SYNC_BATCH_SIZE = 1000;
const API_RETRY_COUNT = 3;
const API_TIMEOUT_SECONDS = 15;
const EXISTING_LOG_ID_LOOKBACK = SYNC_BATCH_SIZE * 2;

const LATEST_LOG_HEADERS = [
  'log_id',
  'received_at',
  'timestamp',
  'client_install_id',
  'client_session_id',
  'upload_batch_id',
  'app_version',
  'build_id',
  'platform',
  'host',
  'consent_version',
  'language',
  'speaker',
  'speaker_display_name',
  'source',
  'text',
  'raw_input',
  'normalized_input',
  'selected_intent',
  'selected_response_id',
  'unknown_words_json',
];

const CLIENT_SUMMARY_HEADERS = [
  'client_install_id',
  'log_count',
  'first_received_at',
  'last_received_at',
  'platforms',
  'hosts',
];

const UNKNOWN_WORDS_HEADERS = [
  'received_at',
  'client_install_id',
  'platform',
  'host',
  'speaker',
  'text',
  'raw_input',
  'unknown_words_json',
  'log_id',
];

const INTENT_SUMMARY_HEADERS = [
  'selected_intent',
  'count',
];

const RAW_LOG_HEADERS = [
  'log_id',
  'received_at',
  'timestamp',
  'client_install_id',
  'client_session_id',
  'upload_batch_id',
  'app_version',
  'build_id',
  'platform',
  'host',
  'consent_version',
  'language',
  'speaker',
  'speaker_display_name',
  'source',
  'text',
  'raw_input',
  'normalized_input',
  'selected_intent',
  'selected_response_id',
  'unknown_words_json',
  'payload_json',
];

function onOpen() {
  ensureSettingsSheet_();
  SpreadsheetApp.getUi()
    .createMenu('ネコルポスログ')
    .addItem('最新ログを取り込み', 'syncLatestLogs')
    .addItem('ユーザー別集計を更新', 'refreshClientSummary')
    .addItem('Unknown Words更新', 'refreshUnknownWords')
    .addItem('Intent集計更新', 'refreshIntentSummary')
    .addSeparator()
    .addItem('全部更新', 'refreshAll')
    .addToUi();
}

function syncLatestLogs() {
  const lock = LockService.getDocumentLock();
  if (!lock.tryLock(30000)) {
    throw new Error('別のログ同期が実行中です。完了後に再実行してください。');
  }

  try {
    ensureSettingsSheet_();

    const params = { limit: SYNC_BATCH_SIZE };
    const lastReceivedAt = getSetting_(LAST_SYNC_KEY);
    const lastLogId = getSetting_(LAST_SYNC_LOG_ID_KEY);
    if (lastReceivedAt && lastLogId) {
      params.cursor_received_at = lastReceivedAt;
      params.cursor_log_id = lastLogId;
    } else if (lastReceivedAt) {
      // 旧バージョンの時刻だけの同期位置からは、重複を避けながら前方へ移行する。
      params.since = lastReceivedAt;
    }

    const rows = fetchAdminApi('/api/admin/export-logs', params);
    const existingIds = getExistingLogIds_('LatestLogs');
    const newRows = rows.filter((row) => {
      const logId = String(row.log_id || '');
      return logId && !existingIds.has(logId);
    });

    writeRowsToSheet('LatestLogs', LATEST_LOG_HEADERS, newRows, true);

    const cursor = getLastCursor_(rows);
    if (cursor) {
      setSettings_({
        [LAST_SYNC_KEY]: cursor.receivedAt,
        [LAST_SYNC_LOG_ID_KEY]: cursor.logId,
      });
    }

    refreshRawLogs_();
  } finally {
    lock.releaseLock();
  }
}

function refreshClientSummary() {
  const rows = fetchAdminApi('/api/admin/client-summary', {});
  writeRowsToSheet('ClientSummary', CLIENT_SUMMARY_HEADERS, rows, false);
}

function refreshUnknownWords() {
  const rows = fetchAdminApi('/api/admin/unknown-words', { limit: 200 });
  writeRowsToSheet('UnknownWords', UNKNOWN_WORDS_HEADERS, rows, false);
}

function refreshIntentSummary() {
  const rows = fetchAdminApi('/api/admin/intent-summary', {});
  writeRowsToSheet('IntentSummary', INTENT_SUMMARY_HEADERS, rows, false);
}

function refreshAll() {
  runRefreshStep_('最新ログ取り込み', syncLatestLogs);
  runRefreshStep_('ユーザー別集計', refreshClientSummary);
  runRefreshStep_('Unknown Words', refreshUnknownWords);
  runRefreshStep_('Intent集計', refreshIntentSummary);
}

function fetchAdminApi(path, params) {
  ensureSettingsSheet_();

  const apiBaseUrl = getSetting_(API_BASE_URL_KEY) || DEFAULT_API_BASE_URL;
  const adminKey = PropertiesService.getScriptProperties().getProperty(ADMIN_KEY_PROPERTY);
  if (!adminKey) {
    throw new Error('Script PropertiesにNEKOLPOS_ADMIN_KEYを設定してください。');
  }

  const query = Object.keys(params || {})
    .filter((key) => params[key] !== null && params[key] !== undefined && String(params[key]) !== '')
    .map((key) => `${encodeURIComponent(key)}=${encodeURIComponent(String(params[key]))}`)
    .join('&');
  const url = `${apiBaseUrl.replace(/\/$/, '')}${path}${query ? `?${query}` : ''}`;

  let lastError;
  for (let attempt = 1; attempt <= API_RETRY_COUNT; attempt += 1) {
    try {
      const response = UrlFetchApp.fetch(url, {
        method: 'get',
        muteHttpExceptions: true,
        timeoutSeconds: API_TIMEOUT_SECONDS,
        headers: {
          'X-Nekolpos-Admin-Key': adminKey,
          Accept: 'application/json',
        },
      });

      const status = response.getResponseCode();
      const body = response.getContentText();
      let json;
      try {
        json = JSON.parse(body);
      } catch (error) {
        throw new Error(`API response is not JSON. status=${status}`);
      }

      if (status >= 200 && status < 300 && json.ok) {
        return Array.isArray(json.rows) ? json.rows : [];
      }

      if (status < 500 && status !== 429) {
        throw new Error(json.error || `API request failed. status=${status}`);
      }
      lastError = new Error(json.error || `API request failed. status=${status}`);
    } catch (error) {
      lastError = error;
    }

    if (attempt < API_RETRY_COUNT) {
      Utilities.sleep(500 * Math.pow(2, attempt - 1));
    }
  }

  throw lastError || new Error('API request failed.');
}

function writeRowsToSheet(sheetName, headers, rows, appendMode) {
  const sheet = getOrCreateSheet_(sheetName);

  if (!appendMode) {
    sheet.clearContents();
  }

  ensureHeader_(sheet, headers);

  if (rows.length > 0) {
    const values = rows.map((row) => headers.map((header) => normalizeCell_(row[header])));
    const startRow = Math.max(sheet.getLastRow() + 1, 2);
    sheet.getRange(startRow, 1, values.length, headers.length).setValues(values);
  }

  ensureSheetFormat_(sheet, headers);
}

function refreshRawLogs_() {
  const rows = fetchAdminApi('/api/admin/export-logs', {
    include_raw: 1,
    limit: 100,
  });
  writeRowsToSheet('RawLogs', RAW_LOG_HEADERS, rows, false);
}

function ensureSettingsSheet_() {
  const sheet = getOrCreateSheet_(SETTINGS_SHEET);
  const defaults = {
    [API_BASE_URL_KEY]: DEFAULT_API_BASE_URL,
    [LAST_SYNC_KEY]: '',
    [LAST_SYNC_LOG_ID_KEY]: '',
  };
  const settings = getSettings_(sheet);
  let changed = false;
  Object.keys(defaults).forEach((key) => {
    if (!Object.prototype.hasOwnProperty.call(settings, key)) {
      settings[key] = defaults[key];
      changed = true;
    }
  });

  if (changed || sheet.getLastRow() < Object.keys(settings).length) {
    writeSettings_(sheet, settings);
  }

  sheet.getRange(1, 1, Math.max(sheet.getLastRow(), 1), 1).setFontWeight('bold');
  sheet.setColumnWidth(1, 220);
  sheet.setColumnWidth(2, 420);
}

function getSetting_(key) {
  const sheet = getOrCreateSheet_(SETTINGS_SHEET);
  return String(getSettings_(sheet)[key] || '').trim();
}

function setSettings_(updates) {
  const sheet = getOrCreateSheet_(SETTINGS_SHEET);
  const settings = getSettings_(sheet);
  Object.keys(updates).forEach((key) => {
    settings[key] = updates[key];
  });
  writeSettings_(sheet, settings);
}

function getSettings_(sheet) {
  if (sheet.getLastRow() < 1) {
    return {};
  }

  const values = sheet.getRange(1, 1, sheet.getLastRow(), 2).getValues();
  return values.reduce((settings, row) => {
    const key = String(row[0] || '').trim();
    if (key) {
      settings[key] = row[1];
    }
    return settings;
  }, {});
}

function writeSettings_(sheet, settings) {
  const values = Object.keys(settings).map((key) => [key, settings[key]]);
  sheet.clearContents();
  sheet.getRange(1, 1, values.length, 2).setValues(values);
}

function getExistingLogIds_(sheetName) {
  const sheet = getOrCreateSheet_(sheetName);
  if (sheet.getLastRow() < 2) {
    return new Set();
  }

  const headers = sheet.getRange(1, 1, 1, Math.max(sheet.getLastColumn(), 1)).getValues()[0];
  const logIdColumn = headers.indexOf('log_id') + 1;
  if (logIdColumn < 1) {
    return new Set();
  }

  // 複合カーソルが通常の重複を防ぐため、再実行時の保険として直近分だけ確認する。
  // 全件走査するとログの蓄積に比例して同期が遅くなり、Apps Scriptの実行上限に達する。
  const dataRowCount = sheet.getLastRow() - 1;
  const rowsToRead = Math.min(dataRowCount, EXISTING_LOG_ID_LOOKBACK);
  const startRow = sheet.getLastRow() - rowsToRead + 1;
  const values = sheet.getRange(startRow, logIdColumn, rowsToRead, 1).getValues();
  return new Set(values.map((row) => String(row[0] || '')).filter((value) => value));
}

function runRefreshStep_(stepName, callback) {
  const startedAt = Date.now();
  console.info(`[refreshAll] ${stepName}を開始します。`);

  try {
    callback();
    console.info(`[refreshAll] ${stepName}が完了しました。 elapsed_ms=${Date.now() - startedAt}`);
  } catch (error) {
    const message = error && error.message ? error.message : String(error);
    throw new Error(`${stepName}に失敗しました: ${message}`);
  }
}

function ensureHeader_(sheet, headers) {
  if (sheet.getLastRow() === 0) {
    sheet.getRange(1, 1, 1, headers.length).setValues([headers]);
    return;
  }

  const currentHeaders = sheet.getRange(1, 1, 1, headers.length).getValues()[0];
  const hasHeader = headers.every((header, index) => currentHeaders[index] === header);
  if (!hasHeader) {
    sheet.getRange(1, 1, 1, headers.length).setValues([headers]);
  }
}

function ensureSheetFormat_(sheet, headers) {
  const lastColumn = headers.length;
  const lastRow = Math.max(sheet.getLastRow(), 1);

  sheet.setFrozenRows(1);
  sheet.getRange(1, 1, 1, lastColumn).setFontWeight('bold');

  if (!sheet.getFilter() && lastRow > 1) {
    sheet.getRange(1, 1, lastRow, lastColumn).createFilter();
  }

  const wrapHeaders = ['text', 'raw_input', 'payload_json'];
  wrapHeaders.forEach((header) => {
    const index = headers.indexOf(header);
    if (index >= 0) {
      sheet.setColumnWidth(index + 1, header === 'payload_json' ? 420 : 280);
    }
  });
}

function getLastCursor_(rows) {
  if (!rows || rows.length === 0) {
    return null;
  }

  const sortedRows = rows
    .filter((row) => row && row.received_at && row.log_id)
    .sort((left, right) => {
      const receivedAtComparison = String(left.received_at).localeCompare(String(right.received_at));
      return receivedAtComparison || String(left.log_id).localeCompare(String(right.log_id));
    });
  if (sortedRows.length === 0) {
    return null;
  }

  const lastRow = sortedRows[sortedRows.length - 1];
  return {
    receivedAt: String(lastRow.received_at),
    logId: String(lastRow.log_id),
  };
}

function getOrCreateSheet_(sheetName) {
  const spreadsheet = SpreadsheetApp.getActiveSpreadsheet();
  return spreadsheet.getSheetByName(sheetName) || spreadsheet.insertSheet(sheetName);
}

function normalizeCell_(value) {
  if (value === null || value === undefined) {
    return '';
  }

  if (typeof value === 'object') {
    return JSON.stringify(value);
  }

  return value;
}
