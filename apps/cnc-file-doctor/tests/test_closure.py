import sys,tempfile,unittest
from pathlib import Path
sys.path[:0]=[str(Path(__file__).resolve().parents[1]/'src'),str(Path(__file__).resolve().parents[1]/'src'/'engine'),str(Path(__file__).resolve().parent/'legacy')]
import closure_check as closure,doctor
from test_dxf_batch import fixture

def line(h,a,b,layer='0'):
    return [(0,'LINE'),(5,h),(330,'1F'),(100,'AcDbEntity'),(8,layer),(100,'AcDbLine'),(10,a[0]),(20,a[1]),(30,0),(11,b[0]),(21,b[1]),(31,0)]

class ClosureTests(unittest.TestCase):
    def audit(self,gap,bend=False):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'part.dxf'
            rows=[line('100',(0,0),(10,0)),line('101',(10,0),(10,10)),line('102',(10,10),(0,10)),line('103',(0,10),(0,gap))]
            if bend:rows.append(line('104',(2,2),(8,8),'UP'))
            p.write_bytes(fixture(rows));before=p.read_bytes();result=closure.audit(p,('0','UP'));self.assertEqual(p.read_bytes(),before);return result
    def test_exact_and_sub_tolerance_closure(self):
        for gap in ('0','0.0009','0.001'):
            with self.subTest(gap=gap):
                r=self.audit(gap);self.assertTrue(r['passed']);self.assertEqual(r['toleranceMm'],'0.001')
    def test_over_tolerance_gap_blocked(self):
        self.assertFalse(self.audit('0.0011')['passed'])
    def test_bends_are_not_required_to_close(self):
        self.assertTrue(self.audit('0',True)['passed'])
    def test_branch_and_tiny_line_are_not_closed_contours(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'tiny.dxf';p.write_bytes(fixture([line('100',(0,0),('0.0005',0))]));self.assertFalse(closure.audit(p,('0',))['passed'])
    def test_final_result_has_closure_gate_and_report(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'s.dxf';source.write_bytes(fixture([line('100',(0,0),(10,0))]))
            f=doctor.run_job([source],p/'out',True)['files'][0]
            self.assertFalse(f['closureCheck']['passed']);self.assertEqual(f['status'],'BLOCKED_REVIEW');self.assertIn('Quarantine',f['candidate'])

if __name__=='__main__':unittest.main()
