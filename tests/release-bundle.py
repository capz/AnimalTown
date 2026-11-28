#!/usr/bin/env python3
"""Exercise release bundling without a console build or network access."""
from pathlib import Path
from unittest.mock import patch
import hashlib
import importlib.util
import io
import json
import subprocess
import tarfile
import tempfile
import unittest
import zipfile

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('bundle',ROOT/'tools/release-bundle.py')
bundle=importlib.util.module_from_spec(spec);spec.loader.exec_module(bundle)

class BundleChecks(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.archive=self.root/'AnimalTown-1.2.3-depot.zip'
        self.runtime={'default.dol':b'compiled executable','content.id':b'build identity','opening.bnr':b'launcher banner'}
        manifest={'Schema':1,'Profile':'sd-app','Commit':'a'*40,'Version':'1.2.3',
                  'Files':{n:hashlib.sha256(b).hexdigest() for n,b in self.runtime.items()}}
        with zipfile.ZipFile(self.archive,'w') as z:
            for n,b in self.runtime.items():z.writestr(bundle.APP+n,b)
            z.writestr(bundle.APP+'depot-package.json',json.dumps(manifest))
        self.reports=self.root/'reports';self.reports.mkdir()
        (self.reports/'BUILD.txt').write_text('source: '+'a'*40)
        (self.reports/'version.json').write_text('{"version":"1.2.3"}')

    def tearDown(self):self.temp.cleanup()

    def attached(self):bundle.attach(self.archive,self.reports)

    def mutate(self,name,data):
        copy=self.root/'changed.zip'
        with zipfile.ZipFile(self.archive) as old,zipfile.ZipFile(copy,'w') as new:
            for entry in old.infolist():
                if entry.filename!=name:new.writestr(entry,old.read(entry))
            new.writestr(name,data)
        self.archive=copy

    def verify(self):
        with zipfile.ZipFile(self.archive) as archive:return bundle.inspect(archive)

    def test_roundtrip_keeps_runtime_and_extracts_only_reports(self):
        self.attached();self.verify();out=self.root/'extracted';bundle.extract(self.archive,out)
        self.assertEqual({p.name:p.read_bytes() for p in out.iterdir()}, {p.name:p.read_bytes() for p in self.reports.iterdir()})
        with zipfile.ZipFile(self.archive) as z:
            for name,data in self.runtime.items():self.assertEqual(z.read(bundle.APP+name),data)
            self.assertNotIn('release/manifest.json',json.loads(z.read(bundle.APP+'depot-package.json'))['Files'])

    def test_append_keeps_prior_scenarios_and_is_idempotent(self):
        self.attached();evidence=self.root/'evidence';evidence.mkdir();(evidence/'frame.png').write_bytes(b'image')
        for namespace in ('cliff-hud','performance','gpu-submit','jobs','villager-visuals','online'):
            bundle.attach(self.archive,evidence,namespace)
        before=self.archive.read_bytes();bundle.attach(self.archive,evidence,'cliff-hud')
        self.assertEqual(before,self.archive.read_bytes())
        _,reports=self.verify();self.assertEqual(len(reports['files']),8)

    def test_terrain_capture_roundtrip_preserves_all_37_frames_and_runtime(self):
        self.attached()
        with zipfile.ZipFile(self.archive) as archive:app_manifest=archive.read(bundle.APP+'depot-package.json')
        # Match the accepted scenario's controls, mip comparisons and water
        # moving/frozen/resumed pairs, including its legacy rendering path.
        names=['normal','menu-off','menu-on','mips-auto','distance-auto','distance-one',
               'mips-one','distance-one-return','distance-ten','mips-ten','distance-ten-return',
               'distance-reset','mips-reset','mipmaps-on','mipmaps-off','mips-disabled',
               'mipmaps-off-return','menu-on-return','menu-restored','restored']
        names += ['water-'+view+'-'+frame for view in ('river','river-frozen','river-resumed',
                  'river-legacy','river-legacy-frozen','river-legacy-resumed','coast') for frame in ('a','b')]
        names += ['water-river-archive-restored','water-menu','water-menu-return']
        screenshots=['terrain-materials-'+name+'.png' for name in names]
        self.assertEqual(len(screenshots),37)
        files={name:('capture '+name).encode() for name in screenshots}
        files['terrain-materials-report.json']=json.dumps({'scenario':'terrain-materials',
            'passed':True,'screenshots':screenshots}).encode()
        files['terrain-materials-build.json']=json.dumps({'schema':1,'commit':'a'*40,'version':'1.2.3',
            'tag':'v1.2.3','isoName':'AnimalTown-1.2.3.nkit.iso','isoSha256':'b'*64}).encode()
        evidence=self.root/'terrain';evidence.mkdir()
        for name,data in files.items():(evidence/name).write_bytes(data)
        bundle.attach(self.archive,evidence)
        _,reports=self.verify()
        self.assertEqual(set(reports['files']),set(files)|{'BUILD.txt','version.json'})
        with zipfile.ZipFile(self.archive) as archive:
            self.assertEqual(archive.read(bundle.APP+'depot-package.json'),app_manifest)
            for name,data in self.runtime.items():self.assertEqual(archive.read(bundle.APP+name),data)
            for name,data in files.items():self.assertEqual(archive.read(bundle.REPORT+name),data)
        out=self.root/'extracted';bundle.extract(self.archive,out)
        for name,data in files.items():self.assertEqual((out/name).read_bytes(),data)
        before=self.archive.read_bytes();bundle.attach(self.archive,evidence)
        self.assertEqual(self.archive.read_bytes(),before)

    def test_daylight_roundtrip_keeps_all_phases_and_runtime(self):
        files={'daylight-build.json':b'{"schema":1}'}
        for phase in ('noon','dawn','dusk','night'):
            for suffix in ('a.png','b.png','report.json'):files['daylight-'+phase+'-'+suffix]=('capture '+phase+suffix).encode()
        for name,data in files.items():(self.reports/name).write_bytes(data)
        self.attached();_,reports=self.verify()
        self.assertTrue(set(files).issubset(reports['files']))
        out=self.root/'daylight';bundle.extract(self.archive,out)
        for name,data in files.items():self.assertEqual((out/name).read_bytes(),data)
        with zipfile.ZipFile(self.archive) as archive:
            for name,data in self.runtime.items():self.assertEqual(archive.read(bundle.APP+name),data)
        self.mutate(bundle.REPORT+'daylight-night-a.png',b'corrupt capture')
        with self.assertRaises(ValueError):self.verify()

    def test_daylight_namespace_rejects_extra_types_and_paths(self):
        for name in ('daylight-noon.py','daylight-noon-a.PNG','daylight-Night-a.png','daylight-midnight-a.png',
                     'daylight-night-extra.json','daylight-night-extra.png','daylight-build.json/source.py','daylight/night-a.png'):
            with self.subTest(name=name):self.assertFalse(bundle.allowed(name))

    def test_video_options_roundtrip_keeps_reports_and_runtime(self):
        names=['video-options-build.json','video-options-smoothing-aa-option.json',
               'video-options-options-title-options.json','video-options-options-title-options-load.json',
               'video-options-unsupported-title-options-unsupported.json',
               'video-options-smoothing-aa-initial-home.png','video-options-smoothing-aa-menu-on.png',
               'video-options-smoothing-aa-menu-off.png','video-options-smoothing-aa-menu-restored.png',
               'video-options-smoothing-aa-576p-rejected.png','video-options-options-options-restored.png',
               'video-options-options-options-delete-default.png','video-options-unsupported-options-unsupported.png']
        for name in names:(self.reports/name).write_bytes(('evidence '+name).encode())
        self.attached();_,reports=self.verify()
        self.assertTrue(set(names).issubset(reports['files']))
        extracted=self.root/'video';bundle.extract(self.archive,extracted)
        for name in names:self.assertEqual((extracted/name).read_bytes(),(self.reports/name).read_bytes())
        with zipfile.ZipFile(self.archive) as z:
            for name,data in self.runtime.items():self.assertEqual(z.read(bundle.APP+name),data)
        self.mutate(bundle.REPORT+'video-options-smoothing-aa-menu-on.png',b'corrupt capture')
        with self.assertRaises(ValueError):self.verify()

    def test_video_options_namespace_rejects_source_and_extra_reports(self):
        for name in ('video-options-extra.json','video-options-smoothing-extra.json','video-options-build.py',
                     'video-options-smoothing-source.cs','video-options-smoothing-aa-menu-on.PNG',
                     'video-options-smoothing-aa-menu-on.png.cs','video-options-unknown-frame.png',
                     'video-options-build.json/source.py','video-options/smoothing/frame.png'):
            with self.subTest(name=name):self.assertFalse(bundle.allowed(name))

    def test_terrain_evidence_namespace_rejects_extra_types_and_paths(self):
        for name in ('terrain-materials-extra.json','terrain-materials-runtime.log','terrain-materials-source.py',
                     'terrain-materials-game.dol','terrain-materials-source.zip','terrain-materials-normal.PNG',
                     'Terrain-materials-normal.png','terrain-materials-normal.png.cs',
                     'nested/terrain-materials-normal.png','terrain-materials/normal.png',
                     'scenarios/terrain-materials/frame.png'):
            with self.subTest(name=name):self.assertFalse(bundle.allowed(name))

    def test_corrupt_terrain_evidence_rejected(self):
        for name in ('terrain-materials-mips-auto.png','terrain-materials-report.json','terrain-materials-build.json'):
            (self.reports/name).write_bytes(b'verified evidence')
        self.attached();original=self.archive
        for name in ('terrain-materials-mips-auto.png','terrain-materials-report.json','terrain-materials-build.json'):
            with self.subTest(name=name):
                self.archive=original;self.mutate(bundle.REPORT+name,b'changed evidence')
                with self.assertRaises(ValueError):self.verify()

    def test_invalid_input_leaves_original_intact(self):
        before=self.archive.read_bytes();(self.reports/'source.cs').write_text('source')
        with self.assertRaises(ValueError):self.attached()
        self.assertEqual(before,self.archive.read_bytes())

    def test_rejects_authoring_source_archives(self):
        for name in ('source.cs','model.glb','provision.py','source.tar.gz','source.zip'):
            with self.subTest(name=name):self.assertFalse(bundle.allowed(name))

    def test_depot_excludes_updater_even_outside_runtime(self):
        before=self.archive.read_bytes()
        (self.reports/'animal-town-updater.dol').write_bytes(b'ISO-only helper')
        with self.assertRaises(ValueError):self.attached()
        self.assertEqual(before,self.archive.read_bytes())
        self.assertFalse(bundle.allowed('animal-town-updater.dol'))

    def test_rejects_bad_paths_and_symlinks(self):
        for name in ('../outside','/absolute','scenarios/performance/../bad.txt','scenarios/performance/a\\b.txt','scenarios/performance/x:bad.txt'):
            with self.subTest(name=name):self.assertFalse(bundle.allowed(name))
        (self.reports/'link.txt').symlink_to(self.reports/'BUILD.txt')
        with self.assertRaises(ValueError):self.attached()

    def test_corrupt_app_rejected(self):
        self.attached();self.mutate(bundle.APP+'default.dol',b'corruption')
        with self.assertRaises(ValueError):self.verify()

    def test_corrupt_report_rejected(self):
        self.attached();self.mutate('release/BUILD.txt',b'corruption')
        with self.assertRaises(ValueError):self.verify()

    def test_missing_and_extra_reports_rejected(self):
        self.attached();self.mutate('release/untracked.txt',b'unknown')
        with self.assertRaises(ValueError):self.verify()

    def test_wrong_identity_rejected(self):
        self.attached()
        with zipfile.ZipFile(self.archive) as z:manifest=json.loads(z.read(bundle.MANIFEST))
        manifest['commit']='b'*40;self.mutate(bundle.MANIFEST,json.dumps(manifest))
        with self.assertRaises(ValueError):self.verify()

    def test_case_collisions_and_archive_symlinks_rejected(self):
        self.attached();original=self.archive
        self.mutate(bundle.APP+'DEFAULT.DOL',b'duplicate')
        with self.assertRaises(ValueError):self.verify()
        self.archive=original
        with zipfile.ZipFile(self.archive,'a') as z:
            item=zipfile.ZipInfo('release/scenarios/performance/link.txt');item.external_attr=0xa1ff0000;z.writestr(item,b'target')
        with self.assertRaises(ValueError):self.verify()

    def test_extraction_refuses_symlink_destination(self):
        self.attached();outside=self.root/'outside';outside.mkdir();out=self.root/'extracted';out.mkdir()
        (out/'BUILD.txt').symlink_to(outside/'BUILD.txt')
        with self.assertRaises(ValueError):bundle.extract(self.archive,out)
        self.assertFalse((outside/'BUILD.txt').exists())

    def test_metadata_budgets(self):
        with zipfile.ZipFile(self.archive,'a') as z:z.writestr('release/manifest.json',b' '*(1024*1024+1))
        with self.assertRaises(ValueError):self.verify()

    def test_remote_digest_checked_before_extracting(self):
        self.attached();data={'draft':False,'assets':[{'name':self.archive.name,'size':self.archive.stat().st_size,
             'digest':'sha256:'+hashlib.sha256(self.archive.read_bytes()).hexdigest()}]}
        with patch.object(bundle.subprocess,'check_output',return_value=json.dumps(data)):
            bundle.verify_remote(self.archive,'capz/AnimalTown','v1.2.3')
        data['assets'][0]['digest']='sha256:'+'0'*64
        with patch.object(bundle.subprocess,'check_output',return_value=json.dumps(data)):
            with self.assertRaises(ValueError):bundle.verify_remote(self.archive,'capz/AnimalTown','v1.2.3')

class SourceArchiveChecks(unittest.TestCase):
    def test_git_zip_and_tar_archives_contain_only_readme(self):
        for kind in ('zip','tar.gz'):
            data=subprocess.check_output(['git','archive','--worktree-attributes','--format='+kind,'HEAD'],cwd=ROOT)
            if kind=='zip':
                with zipfile.ZipFile(io.BytesIO(data)) as archive:names=archive.namelist()
            else:
                with tarfile.open(fileobj=io.BytesIO(data),mode='r:gz') as archive:names=archive.getnames()
            self.assertEqual(names,['README.md'])

if __name__=='__main__':unittest.main()
