import sys,tempfile,unittest
from pathlib import Path
sys.path[:0]=[str(Path(__file__).resolve().parents[1]/'src'),str(Path(__file__).resolve().parents[1]/'src'/'engine'),str(Path(__file__).resolve().parent/'legacy')]
import doctor,polyline_adapter as adapter,dxf_support as dx
from test_dxf_batch import fixture

def poly(flags=1,extra=()):
    return [(0,'LWPOLYLINE'),(5,'100'),(330,'1F'),(100,'AcDbEntity'),(8,'0'),(100,'AcDbPolyline'),(90,4),(70,flags),(10,'0.000000000000001'),(20,'0'),(10,10),(20,0),(10,10),(20,10),(10,'0.000000000000001'),(20,10)]+list(extra)

class PolylineTests(unittest.TestCase):
    def test_closed_decimal_coordinates_and_byte_preservation(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'s.dxf';n=p/'n.dxf';s.write_bytes(fixture([poly()]));before=s.read_bytes()
            proof=adapter.normalize(s,n,('0',));self.assertEqual(proof['lineCount'],4);self.assertTrue(proof['serializedCoordinatesVerified']);self.assertEqual(s.read_bytes(),before)
            pairs,rows=dx.read(n);self.assertEqual(len(rows),4);self.assertEqual(dx.one(rows[0],10),'0.000000000000001');self.assertEqual(dx.one(rows[-1],11),'0.000000000000001')
            handles=[dx.one(r,5) for r in rows];self.assertEqual(len(set(handles)),4)
    def test_open_polyline_is_not_closed(self):
        self.assertEqual(len(adapter.vertices({'pairs':poly(128)})),3)
    def test_refuses_geometry_and_metadata_loss(self):
        for extra in (((42,'0.2'),),((43,'1'),),((38,'1'),),((230,'-1'),),((1000,'custom'),)):
            with self.subTest(extra=extra),self.assertRaises(ValueError):adapter.vertices({'pairs':poly(extra=extra)})
    def test_rejects_bad_vertex_counts_and_zero_length(self):
        tags=poly();tags=[(k,'5' if k==90 else v) for k,v in tags]
        with self.assertRaises(ValueError):adapter.vertices({'pairs':tags})
        with self.assertRaises(ValueError):adapter.vertices({'pairs':[(0,'LWPOLYLINE'),(90,2),(10,0),(20,0),(10,0),(20,0)]})
    def test_full_job_preserves_original_and_reports_lineage(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'s.dxf';s.write_bytes(fixture([poly()]));r=doctor.run_job([s],p/'out',True)
            f=r['files'][0];self.assertTrue(f['sourceUnchanged']);self.assertTrue(f['exactCopyVerified']);self.assertEqual(f['conversion']['normalization']['wholeCurveBoundMm'],'0')
            report=(p/'out'/'Reports'/'report.html').read_text(encoding='utf-8');self.assertIn('Полилинии: 1',report);self.assertNotIn('CHECKED_CANDIDATE_REVIEW_REQUIRED',report)
    def test_bend_polyline_not_silently_converted(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'s.dxf';s.write_bytes(fixture([[(k,'UP' if k==8 else v) for k,v in poly()]]))
            with self.assertRaises(ValueError):adapter.normalize(s,p/'n.dxf',('0',))

if __name__=='__main__':unittest.main()
