import sys,unittest,tempfile
from pathlib import Path
from decimal import Decimal as D
sys.path[:0]=[str(Path(__file__).resolve().parents[1]/'src'),str(Path(__file__).resolve().parents[1]/'src'/'engine'),str(Path(__file__).resolve().parent/'legacy')]
import contour_join as join,dxf_support as dx,doctor
from test_quality_v03 import square
from test_dxf_batch import fixture

class JoinTests(unittest.TestCase):
    def test_square_is_one_closed_object(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'s.dxf';s.write_bytes(fixture([r['pairs'] for r in square('100',0,0,10,10)]));before=s.read_bytes()
            r=join.join(s,p/'joined.dxf');_,rows=dx.read(p/'joined.dxf')
            self.assertEqual(len(rows),1);self.assertEqual(dx.kind(rows[0]),'LWPOLYLINE');self.assertEqual(dx.one(rows[0],70),'1')
            self.assertEqual(r['maximumAdditionalDeviationMm'],'0');self.assertEqual(s.read_bytes(),before)
    def test_arcs_remain_arcs_in_bulges(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'s.dxf';rows=[]
            for i,(a,b) in enumerate(((0,180),(180,360))):rows.append([(0,'ARC'),(5,str(100+i)),(330,'1F'),(100,'AcDbEntity'),(8,'0'),(100,'AcDbCircle'),(10,'0'),(20,'0'),(30,'0'),(40,'10'),(100,'AcDbArc'),(50,str(a)),(51,str(b))])
            s.write_bytes(fixture(rows));r=join.join(s,p/'joined.dxf');_,out=dx.read(p/'joined.dxf')
            self.assertEqual(r['closedPolylineCount'],1);self.assertEqual(len(out),1);self.assertEqual(len(join.poly_curves(out[0])),4)
            self.assertTrue(all(v!='0' for v in dx.values(out[0],42)));self.assertLess(D(r['maximumAdditionalDeviationMm']),D('.000001'))
    def test_open_contour_is_not_closed_with_added_connector(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'s.dxf';s.write_bytes(fixture([r['pairs'] for r in square('100',0,0,10,10)[:3]]))
            with self.assertRaises(ValueError):join.join(s,p/'out.dxf')
            self.assertFalse((p/'out.dxf').exists())
    def test_different_styles_refused_without_modification(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'s.dxf';rows=square('100',0,0,10,10);rows[0]['pairs'].append((62,'1'));s.write_bytes(fixture([r['pairs'] for r in rows]));before=s.read_bytes()
            with self.assertRaises(ValueError):join.join(s,p/'out.dxf')
            self.assertEqual(s.read_bytes(),before)
    def test_conversion_uses_joined_result_and_keeps_analytic_reference(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'s.dxf';s.write_bytes(fixture([r['pairs'] for r in square('100',0,0,10,10)]))
            f=doctor.run_job([s],p/'result',convert=True)['files'][0]
            self.assertEqual(f['status'],'CHECKED_CANDIDATE_REVIEW_REQUIRED');self.assertTrue(f['contourJoin']['verified'])
            self.assertTrue(f['candidate'].endswith('-CONTOURS.dxf'));self.assertTrue(Path(f['analyticCandidate']).exists())

if __name__=='__main__':unittest.main()
