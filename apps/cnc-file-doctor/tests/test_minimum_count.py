import sys,unittest
from unittest.mock import patch
from pathlib import Path
from decimal import Decimal as D
from fractions import Fraction as F
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'src'/'engine'))
import minimum_count as mc,spline_arc_core as core,certified_curves as c

class CountTests(unittest.TestCase):
    def test_global_graph_path_beats_longest_local_merge(self):
        # Longest first would choose 0->4->5->6 (3); optimum is 0->3->6 (2).
        edges={(i,i+1):dict(bound=D('.001'),edge=(i,i+1)) for i in range(6)}
        for a,b in ((0,4),(0,3),(3,6)):edges[a,b]=dict(bound=D('.002'),edge=(a,b))
        self.assertEqual([p['edge'] for p in mc.shortest_path(6,edges)],[(0,3),(3,6)])
    def test_four_certified_arc_pieces_reduce_to_one(self):
        seg=core.spans(2,[0,0,0,1,1,1],[(1,0),(1,1),(0,1)],[1,'0.7071067811865476',1])[0]
        a,b=c.split(seg['h']);hs=[q for h in (a,b) for q in c.split(h)]
        options=core.Options();budget=core.Budget(options)
        pieces=[core.attempt([dict(h=h,domain=(F(i,4),F(i+1,4)))],budget) for i,h in enumerate(hs)]
        self.assertTrue(all(pieces));out,proof,_=mc.optimize(pieces,options)
        self.assertEqual(len(out),1);self.assertEqual(out[0]['primitive']['type'],'ARC')
        self.assertLessEqual(out[0]['bound'],options.tolerance_mm);self.assertFalse(proof['globalMinimumProven'])
    def test_original_corner_and_budget_exhaustion_keep_valid_path(self):
        options=core.Options();budget=core.Budget(options)
        pieces=[]
        for a,b,left,right in (((0,0),(1,0),0,F(1,2)),((1,0),(1,1),F(1,2),1)):
            h=[(F(a[0]),F(a[1]),F(1)),(F(b[0]),F(b[1]),F(1))]
            pieces.append(core.attempt([dict(h=h,domain=(left,right))],budget))
        out,proof,_=mc.optimize(pieces,options);self.assertEqual(len(out),2)
        pieces=[]
        for i in range(3):
            h=[(F(i),F(0),F(1)),(F(i+1),F(0),F(1))]
            pieces.append(core.attempt([dict(h=h,domain=(F(i,3),F(i+1,3)))],budget))
        with patch.object(core.Budget,'check',side_effect=core.ResourceLimit('Test exhausted budget')):
            out,proof,_=mc.optimize(pieces,options)
        self.assertEqual(len(out),3)
        self.assertTrue(proof['resourceLimitReached']);self.assertFalse(proof['completeCandidateSearch'])

if __name__=='__main__':unittest.main()
