#!/usr/bin/env python3
"""Compile and test exact pure portions of the current Editor Runner without Unity."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

RUNNER = Path('Assets/Scripts/Editor/DebugScenarioEditorRunner.cs')
INPUT = Path('Assets/Scripts/System/DebugConsoleInput.cs')
CHAT = Path('Assets/Scripts/System/ChatUIController.cs')

def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def extract(text: str, start: str, end: str) -> str:
    if text.count(start) != 1 or text.count(end) != 1:
        raise ValueError('Runner extraction boundaries changed; review this tool before continuing.')
    a = text.index(start)
    b = text.index(end, a)
    return text[a:b]

def reject_probe(reject: str, name: str) -> str:
    return '''public static class NAME {
    const string BatchKey="batch", LastKey="last";
    public static System.Collections.Generic.Dictionary<string,string> memory = new();
    public static bool failSave;
    public sealed class Report { public string status, automatedStatus, reason, phase, finishedUtc, reportPath="report"; }
    private static class SessionState {
        public static void EraseString(string key) => memory.Remove(key);
        public static void SetString(string key,string value) => memory[key]=value;
    }
    private static void Save(Report report) { if(failSave) throw new InvalidOperationException("Fake report failure"); }
    public static string Run() => Reject(new Report(),"EnvironmentUnavailable","fake rejection");
    REJECT
}
'''.replace('NAME', name).replace('REJECT', reject)

def method(text: str, signature: str) -> str:
    if text.count(signature) != 1:
        raise ValueError('Input method boundary changed; review extraction before continuing.')
    start = text.index(signature)
    brace = text.index('{', start)
    depth = 0
    for end in range(brace, len(text)):
        if text[end] == '{': depth += 1
        elif text[end] == '}':
            depth -= 1
            if depth == 0: return text[start:end+1]
    raise ValueError('Input method has an unmatched brace.')

def input_probe(input_text: str, chat_text: str, legacy: str) -> str:
    lines = '\n'.join(line.strip() for line in input_text.split('#else')[0].splitlines()
        if line.strip().startswith(('public static bool IsConsoleBlocked =>', 'public static bool IsBlocked =>', 'public static bool ShouldBlock(')))
    if 'IsConsoleBlocked' not in lines or lines.count('public static bool') != 3:
        raise ValueError('Console input predicates changed; review extraction before continuing.')
    methods = '\n'.join(method(chat_text, signature) for signature in (
        '        private void HandleNextInput()', '        private bool HandleNextInputCore()',
        '        public bool AdvanceIsolatedDebugMessage()', '        public bool AdvanceIsolatedScenarioMessage()'))
    return '''using System;
public static class Time { public static int frameCount=10; }
public static class DebugSaveSession { public static bool CanRunScenarios=true; }
public static class DebugScenarioExecutor { public static bool IsRunning=true; }
public sealed class Panel { public bool activeSelf; }
public static class DebugConsoleInput {
    public static bool owned; public static object activeLease; public static int releasedFrame=-1;
    INPUT_BODY
}
public sealed class ChatUIController {
    public bool menuInputBlocked, isTyping, skipTyping;
    public object delayedDialogueProcessCoroutine;
    public Panel choicePanel=new Panel(); public event Action OnWaitInputCompleted;
    public void Normal() => HandleNextInput();
    CHAT_BODY
}
'''.replace('INPUT_BODY', lines).replace('CHAT_BODY', methods) + legacy

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo-root', type=Path, default=Path(__file__).resolve().parents[3])
    args = parser.parse_args()
    root = args.repo_root.resolve()
    own = Path(__file__).resolve().parent
    source = root / RUNNER
    raw = source.read_bytes()
    input_raw = (root / INPUT).read_bytes()
    chat_raw = (root / CHAT).read_bytes()
    text = raw.decode('utf-8-sig')
    pure = extract(text, '        // BEGIN MEMORY-ONLY SCENARIO AWAIT', '        // END MEMORY-ONLY SCENARIO AWAIT')
    reject = extract(text, '        private static string Reject(', '        private static void Save(')
    input_source = input_probe(input_raw.decode('utf-8-sig'), chat_raw.decode('utf-8-sig'),
                               (own / 'legacy_input.fixture.cs').read_text(encoding='utf-8'))
    out = root / 'output/debug-scenario-runner-checks'
    out.mkdir(parents=True, exist_ok=True)
    (out / 'PureAwait.cs').write_text('using System;\nusing System.Linq;\nusing System.Threading;\npublic static class PureAwait {\n' + pure + '\n}\n', encoding='utf-8')
    legacy = (own / 'legacy_reject.fixture.cs').read_text(encoding='utf-8')
    (out / 'RejectProbe.cs').write_text('using System;\n' + reject_probe(reject, 'RejectProbe') + reject_probe(legacy, 'OldRejectProbe'), encoding='utf-8')
    (out / 'InputExtracted.cs').write_text(input_source, encoding='utf-8')
    for name in ('Program.cs', 'MemoryOnly.csproj', 'LegacyBatch.cs', 'InputProgram.cs', 'InputChecks.csproj'):
        shutil.copyfile(own / name, out / name)
    env = os.environ.copy()
    env['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    env['DOTNET_NOLOGO'] = '1'
    version = subprocess.run(['dotnet', '--version'], check=True, capture_output=True, text=True, timeout=30, env=env).stdout.strip()
    run = subprocess.run(['dotnet', 'run', '--project', str(out / 'MemoryOnly.csproj'), '--configuration', 'Release'],
                         cwd=out, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=120, env=env)
    input_run = subprocess.run(['dotnet', 'run', '--project', str(out / 'InputChecks.csproj'), '--configuration', 'Release'],
                         cwd=out, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=120, env=env)
    (out / 'build-run.log').write_text(run.stdout + run.stderr + input_run.stdout + input_run.stderr, encoding='utf-8')
    stable = source.read_bytes() == raw and (root / INPUT).read_bytes() == input_raw and (root / CHAT).read_bytes() == chat_raw
    result_file = out / 'memory-only-results.json'
    results = json.loads(result_file.read_text(encoding='utf-8')) if run.returncode == 0 and result_file.exists() else []
    input_file = out / 'input-results.json'
    input_results = json.loads(input_file.read_text(encoding='utf-8')) if input_run.returncode == 0 and input_file.exists() else []
    passed = bool(results) and bool(input_results) and all(item.get('passed') is True for item in results + input_results) and stable and run.returncode == 0 and input_run.returncode == 0
    manifest = {'runner': RUNNER.as_posix(), 'runner_sha256': sha(raw), 'source_unchanged': stable,
                'helper_sha256': sha(pure.encode('utf-8')), 'dotnet_sdk': version,
                'passed': passed, 'cases': len(results) + len(input_results), 'runner_cases': len(results),
                'input_cases': len(input_results), 'unity_executed': False,
                'normal_save_adapter_executed': False, 'process_exit_code': run.returncode,
                'verifier_sha256': sha(Path(__file__).read_bytes()),
                'harness_sha256': sha((own / 'Program.cs').read_bytes())}
    manifest.update({'input_sha256': sha(input_raw), 'chat_sha256': sha(chat_raw),
                     'input_process_exit_code': input_run.returncode,
                     'input_harness_sha256': sha((own / 'InputProgram.cs').read_bytes())})
    (out / 'verification-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(json.dumps(manifest, indent=2))
    if not passed:
        print('Verification failed. Inspect output/debug-scenario-runner-checks/build-run.log.', file=sys.stderr)
    return 0 if passed else 1

if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f'Environment or extraction unavailable: {type(error).__name__}: {error}', file=sys.stderr)
        raise SystemExit(2)
