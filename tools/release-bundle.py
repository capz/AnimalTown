#!/usr/bin/env python3
"""Keep release metadata/evidence inside the Depot ZIP, outside its installed app.

The app payload stays immutable; the Depot registry moves only apps/animaltown/.
No source files or authoring kits are accepted in the supplementary payload.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import zipfile

APP='apps/animaltown/'
REPORT='release/'
MANIFEST=REPORT+'manifest.json'
FIXED={'BUILD.txt','version.json','texture-audit.json','validation-plan.json','nkit-validation.json',
       'opening.bnr','opening-pal.bnr','SHA256SUMS.txt','update.json','animal-town-updater.dol',
       'updater-THIRD-PARTY-NOTICES.txt','release-notes.md','updater-setup.md'}
EVIDENCE={'title-logo.png','title-logo-picker.png','title-logo-name.png','title-logo-return.png',
          'title-logo-debug.png','title-logo-status.png','title-logo-report.json','title-logo-build.json',
          'terrain-materials-report.json','terrain-materials-build.json'}
MAX_FILES=4096
MAX_BYTES=2*1024**3

def sha(stream):
    return hashlib.file_digest(stream,'sha256').hexdigest()

def safe(name):
    return bool(name) and '\\' not in name and ':' not in name and all(32<=ord(c)<127 for c in name) and all(p not in ('','.','..') for p in name.split('/'))

def allowed(name):
    if not safe(name):return False
    if name in FIXED|EVIDENCE:return True
    # The publisher validates this scenario's reported captures and exact
    # source/ISO identity before attaching them. Keep its flat PNG namespace.
    if re.fullmatch(r'terrain-materials-[a-z0-9-]+\.png',name):return True
    parts=name.split('/')
    return len(parts)==3 and parts[0]=='scenarios' and parts[1] in ('cliff-hud','gpu-submit','performance') and Path(parts[2]).suffix in ('.png','.json','.txt','.log','.avi')

def inspect(archive, require_reports=True):
    entries=archive.infolist()
    if len(entries)>MAX_FILES or sum(e.file_size for e in entries)>MAX_BYTES:raise ValueError('Release ZIP exceeds its file/byte budget')
    seen=set()
    for e in entries:
        n=e.filename
        if not safe(n) or n.casefold() in seen or (e.external_attr>>16)&0xf000==0xa000:raise ValueError('Unsafe/duplicate ZIP path: '+n)
        seen.add(n.casefold())
        if n.startswith(APP):
            if '/' in n[len(APP):]:raise ValueError('App payload must stay flat')
        elif n!=MANIFEST and not (n.startswith(REPORT) and allowed(n[len(REPORT):])):raise ValueError('Unexpected release ZIP entry: '+n)
    if archive.getinfo(APP+'depot-package.json').file_size>1024*1024:raise ValueError('Oversized app manifest')
    app=json.loads(archive.read(APP+'depot-package.json'))
    if app['Schema']!=1 or app['Profile']!='sd-app':raise ValueError('Invalid app manifest')
    expected={APP+n for n in app['Files']}|{APP+'depot-package.json'}
    if expected!={e.filename for e in entries if e.filename.startswith(APP)}:raise ValueError('Unexpected app file')
    for name,digest in app['Files'].items():
        with archive.open(APP+name) as f:
            if sha(f)!=digest:raise ValueError('App checksum mismatch: '+name)
    reports=None
    if MANIFEST in archive.namelist():
        if archive.getinfo(MANIFEST).file_size>1024*1024:raise ValueError('Oversized release manifest')
        reports=json.loads(archive.read(MANIFEST))
        if reports['schema']!=1 or reports['commit']!=app['Commit'] or reports['version']!=app['Version']:raise ValueError('Release/app identity mismatch')
        wanted={REPORT+n for n in reports['files']}|{MANIFEST}
        if wanted!={e.filename for e in entries if e.filename.startswith(REPORT)}:raise ValueError('Unexpected release report')
        for name,digest in reports['files'].items():
            if not allowed(name):raise ValueError('Source or unsupported report: '+name)
            with archive.open(REPORT+name) as f:
                if sha(f)!=digest:raise ValueError('Report checksum mismatch: '+name)
    elif require_reports:raise ValueError('This older app ZIP has no release report bundle')
    return app,reports

def attach(archive_path, folder, namespace=''):
    archive_path=Path(archive_path);folder=Path(folder)
    if namespace and namespace not in ('cliff-hud','gpu-submit','performance'):raise ValueError('Invalid evidence namespace')
    additions={}
    for p in sorted(folder.rglob('*')):
        if p.is_symlink():raise ValueError('Symlink report input')
        if not p.is_file():continue
        name=p.relative_to(folder).as_posix()
        if namespace:name='scenarios/'+namespace+'/'+name
        if not allowed(name):raise ValueError('Source or unsupported report input: '+name)
        additions[REPORT+name]=p
    if not additions:raise ValueError('No release reports supplied')
    descriptor,temporary=tempfile.mkstemp(prefix=archive_path.name+'.',suffix='.tmp',dir=archive_path.parent);os.close(descriptor)
    try:
        with zipfile.ZipFile(archive_path) as old,zipfile.ZipFile(temporary,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as new:
            app,reports=inspect(old,require_reports=False)
            hashes=dict(reports['files']) if reports else {}
            added_hashes={}
            for name,p in additions.items():
                with p.open('rb') as source:added_hashes[name[len(REPORT):]]=sha(source)
            if reports and all(hashes.get(n)==h for n,h in added_hashes.items()):return
            for item in old.infolist():
                if item.filename==MANIFEST or item.filename in additions:continue
                with old.open(item) as source,new.open(item,'w') as destination:shutil.copyfileobj(source,destination,65536)
            for name,p in additions.items():
                hashes[name[len(REPORT):]]=added_hashes[name[len(REPORT):]]
                item=zipfile.ZipInfo(name,date_time=(2000,1,1,0,0,0));item.compress_type=zipfile.ZIP_DEFLATED;item.external_attr=0
                with p.open('rb') as source,new.open(item,'w') as destination:shutil.copyfileobj(source,destination,65536)
            value={'schema':1,'commit':app['Commit'],'version':app['Version'],'files':dict(sorted(hashes.items()))}
            item=zipfile.ZipInfo(MANIFEST,date_time=(2000,1,1,0,0,0));item.compress_type=zipfile.ZIP_DEFLATED;item.external_attr=0
            new.writestr(item,json.dumps(value,sort_keys=True,indent=2)+'\n')
        with zipfile.ZipFile(temporary) as ready:inspect(ready)
        os.replace(temporary,archive_path)
    finally:
        if os.path.exists(temporary):os.unlink(temporary)

def extract(archive_path,destination):
    destination=Path(destination);destination.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(archive_path) as archive:
        _,reports=inspect(archive)
        for name in reports['files']:
            target=destination/name
            if target.is_symlink() or not target.resolve().is_relative_to(destination.resolve()):raise ValueError('Symlink extraction target')
            target.parent.mkdir(parents=True,exist_ok=True)
            with archive.open(REPORT+name) as source,target.open('wb') as output:shutil.copyfileobj(source,output,65536)

def verify_remote(archive_path,repository,tag):
    """Verify GitHub's digest before using metadata from the containing ZIP."""
    metadata=json.loads(subprocess.check_output(['gh','api',f'repos/{repository}/releases/tags/{tag}'],text=True))
    matches=[a for a in metadata['assets'] if a['name']==Path(archive_path).name]
    if metadata['draft'] or len(matches)!=1:raise ValueError('Published Depot asset missing/ambiguous')
    asset=matches[0]
    with Path(archive_path).open('rb') as source:digest=sha(source)
    if asset['size']!=Path(archive_path).stat().st_size or asset.get('digest')!='sha256:'+digest:raise ValueError('Published Depot asset digest mismatch')

def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    add=sub.add_parser('attach');add.add_argument('archive',type=Path);add.add_argument('folder',type=Path);add.add_argument('--namespace',default='')
    get=sub.add_parser('extract');get.add_argument('archive',type=Path);get.add_argument('destination',type=Path)
    get.add_argument('--repository');get.add_argument('--tag')
    check=sub.add_parser('verify');check.add_argument('archive',type=Path)
    args=parser.parse_args()
    if args.command=='attach':attach(args.archive,args.folder,args.namespace)
    elif args.command=='extract':
        if bool(args.repository)!=bool(args.tag):parser.error('--repository and --tag must be supplied together')
        if args.repository:verify_remote(args.archive,args.repository,args.tag)
        extract(args.archive,args.destination)
    else:
        with zipfile.ZipFile(args.archive) as archive:inspect(archive)
    print('Release bundle '+args.command+' passed')

if __name__=='__main__':main()
