"""Extract exact PlayerSafety audio-hook methods and check their observable calls.

Python 3.10+ / .NET SDK 10. Physics/input/transform/audio are memory doubles.
No Unity/real audio/settings/persistence. No Git or pre-existing audit output needed.
"""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import uuid


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def masked_code(source: str) -> str:
    """Preserve offsets while hiding comments and ordinary string/character literals."""
    result = list(source)
    i = 0
    while i < len(source):
        end = None
        if source.startswith('//', i):
            found = source.find('\n', i + 2)
            end = len(source) if found < 0 else found
        elif source.startswith('/*', i):
            found = source.find('*/', i + 2)
            if found < 0: raise ValueError('Unterminated comment')
            end = found + 2
        elif source.startswith('$"', i) or source.startswith('$@"', i) or source.startswith('@$"', i) or source.startswith('"""', i):
            raise ValueError('Interpolated/raw string syntax needs separate extractor review')
        elif source[i] in ('"', "'") or source.startswith('@"', i):
            verbatim = source.startswith('@"', i)
            quote = '"' if verbatim else source[i]
            cursor = i + (2 if verbatim else 1)
            while cursor < len(source):
                if not verbatim and source[cursor] == '\\':
                    cursor += 2
                    continue
                if source[cursor] == quote:
                    if verbatim and cursor + 1 < len(source) and source[cursor + 1] == quote:
                        cursor += 2
                        continue
                    end = cursor + 1
                    break
                cursor += 1
            if end is None: raise ValueError('Unterminated literal')
        if end is None:
            i += 1
        else:
            for index in range(i, end):
                if source[index] not in '\r\n': result[index] = ' '
            i = end
    return ''.join(result)


def extract_method(source: str, masked: str, name: str) -> str:
    matches = list(re.finditer(r'^    (?:private|public|internal) (?:static )?[^\n=;{}]+\b' + re.escape(name) + r'\(', masked, re.M))
    if len(matches) != 1: raise ValueError('Required method missing or ambiguous: ' + name)
    found = matches[0]
    opening = masked.find('{', found.end())
    arrow = masked.find('=>', found.end())
    if arrow >= 0 and (opening < 0 or arrow < opening):
        end = masked.find(';', arrow)
        if end < 0: raise ValueError('Incomplete expression method')
        return source[found.start():end + 1]
    if opening < 0: raise ValueError('Missing method body')
    depth = 0
    for index in range(opening, len(masked)):
        if masked[index] == '{': depth += 1
        elif masked[index] == '}':
            depth -= 1
            if depth == 0: return source[found.start():index + 1]
    raise ValueError('Unbalanced method')


def extract_property(source: str, masked: str, name: str) -> str:
    matches = list(re.finditer(r'^    public bool ' + re.escape(name) + r' =>', masked, re.M))
    if len(matches) != 1: raise ValueError('Required property missing or ambiguous: ' + name)
    end = masked.find(';', matches[0].end())
    if end < 0: raise ValueError('Incomplete property')
    return source[matches[0].start():end + 1]


def main() -> int:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo-root', type=Path, default=here.parents[2])
    parser.add_argument('--source', type=Path, help='PlayerSafetyMovementController.cs to inspect; defaults to selected checkout')
    parser.add_argument('--output', type=Path, help='New directory under selected repo/output')
    args = parser.parse_args()
    repo = args.repo_root.resolve()
    source_path = (args.source or repo/'Assets/Scripts/Camera/PlayerSafetyMovementController.cs').resolve()
    run = (args.output or repo/'output/player-movement-audio-checks'/(datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ-')+uuid.uuid4().hex[:8])).resolve()
    run.relative_to((repo/'output').resolve())
    run.mkdir(parents=True, exist_ok=False)
    manifest_path = run/'verification-manifest.json'
    manifest = {'completed':False,'passed':False,'status':'incomplete','startedUtc':datetime.now(timezone.utc).isoformat(),'sourceFile':source_path.name,'unityLoaded':False,'realPhysicsOrAudio':False,'realSettingsOrPersistence':False,'missingTypes':[]}

    def save():
        temporary = run/'manifest.pending.json'
        temporary.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
        temporary.replace(manifest_path)

    save()
    try:
        original = source_path.read_bytes()
        program = (here/'Program.cs').read_bytes()
        verifier = Path(__file__).read_bytes()
        manifest.update(sourceSha256=sha(original),programSha256=sha(program),verifierSha256=sha(verifier))
        (run/'source-snapshot.cs').write_bytes(original)
        source = original.decode('utf-8-sig').replace('\r\n','\n')
        masked = masked_code(source)
        extended = bool(re.search(r'^    private void StartJump\(',masked,re.M))
        names = ['Update','TeleportTo','TryStartJump','OnDisable','CalculateJumpVelocity','CanStartJump']
        names.append('StartJump' if extended else 'RefreshWalkableState')
        pieces = {name:extract_method(source,masked,name) for name in names}
        pieces['CanJump'] = extract_property(source,masked,'CanJump')
        if extended: pieces['IsGroundedForTabletFootInput'] = extract_property(source,masked,'IsGroundedForTabletFootInput')
        manifest['extractedMembers'] = {name:sha(body.encode('utf-8')) for name,body in pieces.items()}
        manifest['extendedRootState'] = extended
        (run/'Selected.cs').write_text('using UnityEngine;\npublic sealed partial class SelectedProbe : ProbeEnvironment\n{\n'+'\n\n'.join(pieces.values())+'\n}\npublic static class ProbeContract { public const bool UsesExtendedRootState = '+str(extended).lower()+'; }\n',encoding='utf-8')
        (run/'Program.cs').write_bytes(program)
        project=run/'HookChecks.csproj'
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Include="Selected.cs"/><Compile Include="Program.cs"/></ItemGroup></Project>\n',encoding='utf-8')
        config=run/'NuGet.Config'
        config.write_text('<configuration><packageSources><clear /></packageSources></configuration>\n',encoding='utf-8')
        dotnet=shutil.which('dotnet')
        if dotnet is None: raise RuntimeError('.NET SDK 10 required')
        env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_NOLOGO='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
        sdk=subprocess.check_output([dotnet,'--version'],env=env,text=True,timeout=30).strip()
        if not re.fullmatch(r'10\.[0-9]+\.[0-9]+(?:[-+].*)?',sdk): raise RuntimeError('.NET SDK 10 required')
        manifest['sdk']=sdk
        for phase,command in [('restore',[dotnet,'restore',str(project),'--configfile',str(config)]),('compile',[dotnet,'build',str(project),'-c','Release','--no-restore','--nologo']),('checks',[dotnet,str(run/'bin/Release/net10.0/HookChecks.dll')])]:
            done=subprocess.run(command,cwd=run,env=env,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=120)
            (run/(phase+'.stdout.txt')).write_text(done.stdout,encoding='utf-8')
            (run/(phase+'.stderr.txt')).write_text(done.stderr,encoding='utf-8')
            manifest[phase+'Exit']=done.returncode
            if phase=='compile': manifest['missingTypes']=sorted(set(re.findall(r"error CS0246: The type or namespace name '([^']+)'",done.stdout+done.stderr)))
            if phase!='checks' and done.returncode!=0: raise RuntimeError('Compile preparation failed')
            if phase=='checks':
                result=json.loads(done.stdout)
                rows=result.get('results')
                if type(result.get('count')) is not int or result['count']!=27 or not isinstance(rows,list) or len(rows)!=27: raise ValueError('Invalid result count')
                if any(not isinstance(row,dict) or not isinstance(row.get('name'),str) or not row['name'] or type(row.get('passed')) is not bool for row in rows): raise ValueError('Invalid case result')
                failed=[row['name'] for row in rows if not row['passed']]
                if len({row['name'] for row in rows})!=27 or type(result.get('failures')) is not int or result['failures']!=len(failed) or done.returncode!=(1 if failed else 0): raise ValueError('Inconsistent result')
                manifest.update(count=27,failures=len(failed),failedNames=failed)
                (run/'results.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
        manifest['sourceUnchanged']=source_path.read_bytes()==original
        manifest['toolSourcesUnchanged']=(here/'Program.cs').read_bytes()==program and Path(__file__).read_bytes()==verifier
        manifest['passed']=manifest['failures']==0 and manifest['sourceUnchanged'] and manifest['toolSourcesUnchanged']
        manifest['completed']=True
        manifest['status']='passed' if manifest['passed'] else 'failed'
        manifest['finishedUtc']=datetime.now(timezone.utc).isoformat()
        save()
        print(json.dumps({'manifest':str(manifest_path.relative_to(repo)),'completed':True,'passed':manifest['passed'],'count':manifest['count'],'failures':manifest['failures']}))
        return 0 if manifest['passed'] else 1
    except Exception as error:
        manifest['errorType']=type(error).__name__
        save()
        print(json.dumps({'completed':False,'passed':False,'errorType':type(error).__name__}),file=sys.stderr)
        return 2


if __name__=='__main__':
    try: raise SystemExit(main())
    except (OSError,ValueError) as error:
        print(json.dumps({'completed':False,'passed':False,'errorType':type(error).__name__}),file=sys.stderr)
        raise SystemExit(2)
