import sys,unittest,tempfile
from pathlib import Path
sys.path[:0]=[str(Path(__file__).resolve().parents[1]/'src'),str(Path(__file__).resolve().parents[1]/'src'/'engine'),str(Path(__file__).resolve().parent/'legacy')]
import topology_certification as topo
import certified_curves as c
import dxf_support as dx
import doctor
from test_quality_v03 import square,curves
from test_dxf_batch import fixture

class ContourTests(unittest.TestCase):
    def test_mixed_reversed_shuffled_edges_have_consistent_winding(self):
        hs=curves(square('A',0,0,10,10))
        hs=[hs[2],list(reversed(hs[0])),hs[3],list(reversed(hs[1]))]
        w,_=topo.winding(hs,(c.I(5),c.I(5)))
        self.assertEqual(abs(w),1)
    def test_mixed_arc_directions_find_hole(self):
        def circle(x,y,r):return dx.conics(dict(type='CIRCLE',cx=str(x),cy=str(y),r=str(r)))
        outer=circle(0,0,10);inner=circle(0,0,2)
        outer=[h if i%2 else list(reversed(h)) for i,h in enumerate(outer)][::-1]
        r=topo.hierarchy([inner,outer])
        self.assertEqual(r['roles'],['HOLE','OUTER']);self.assertEqual(r['parents'],[1,None])
    def test_nested_islands_and_separate_outer(self):
        rows=square('A',0,0,20,20)+square('B',2,2,10,10)+square('C',3,3,5,5)+square('D',30,0,40,10)
        r=topo.classify(rows)
        self.assertTrue(r['verified']);self.assertEqual(r['outerCount'],2);self.assertEqual(r['holeCount'],1);self.assertEqual(r['islandCount'],1)
        self.assertEqual([v['role'] for v in r['contours']],['OUTER','HOLE','ISLAND','OUTER'])
    def test_intersection_is_not_classified_as_valid_hole(self):
        r=topo.classify(square('A',0,0,10,10)+square('B',5,5,15,15))
        self.assertFalse(r['verified'])
    def test_inspect_reports_outer_and_hole_without_modifying_source(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);s=p/'part.dxf';data=fixture([r['pairs'] for r in square('A',0,0,10,10)+square('B',2,2,4,4)]);s.write_bytes(data)
            f=doctor.run_job([s],p/'result')['files'][0]
            self.assertEqual(f['contourClassification']['roles'],['OUTER','HOLE'])
            self.assertTrue(any('Contours identified' in text for text in f['findingsEn']))
            self.assertEqual(s.read_bytes(),data)

if __name__=='__main__':unittest.main()
