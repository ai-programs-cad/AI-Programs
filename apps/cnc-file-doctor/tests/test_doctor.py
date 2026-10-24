import json, sys, tempfile, unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'src'))
sys.path.insert(0,str(Path(__file__).resolve().parent/'legacy'))
import doctor
from test_dxf_batch import fixture, spline

class DoctorTests(unittest.TestCase):
    def test_malformed_rejected(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'bad.dxf';p.write_bytes(b'0\nSECTION\n2\nENTITIES\n0\nEOF\n')
            with self.assertRaises(ValueError):doctor.scan(p)
    def test_inspect_preserves_source_and_copy(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'part.dxf';s.write_bytes(fixture([spline('100')]))
            before=s.read_bytes();r=doctor.run_job([s],p/'out')
            self.assertEqual(s.read_bytes(),before);self.assertTrue(r['files'][0]['exactCopyVerified'])
            self.assertTrue((p/'out'/'Reports'/'report.html').exists())
    def test_conversion_quarantines_unverified_output(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'part.dxf';s.write_bytes(fixture([spline('100')]))
            r=doctor.run_job([s],p/'out',True)
            item=r['files'][0];self.assertEqual(item['status'],'BLOCKED_REVIEW');self.assertTrue(item['sourceUnchanged'])
            self.assertTrue(Path(item['candidate']).exists());self.assertFalse(list((p/'out'/'CAM_ARCS').iterdir()))
    def test_output_overlap_and_existing_folder_rejected(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'part.dxf';s.write_bytes(fixture([spline('100')]))
            with self.assertRaises(ValueError):doctor.run_job([p],p/'out')
            (p/'existing').mkdir()
            with self.assertRaises(FileExistsError):doctor.run_job([s],p/'existing')
    def test_same_filename_and_bad_file_batch(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);(p/'a').mkdir();(p/'b').mkdir()
            for d in ('a','b'):(p/d/'part.dxf').write_bytes(fixture([spline('100')]))
            bad=p/'bad.dxf';bad.write_bytes(b'broken')
            r=doctor.run_job([p/'a',p/'b',bad],p/'out')
            self.assertEqual(len(r['files']),3);self.assertEqual(len(list((p/'out'/'EXACT').iterdir())),2)
            self.assertEqual(sum(f['status']=='ERROR' for f in r['files']),1)

if __name__=='__main__':unittest.main()
