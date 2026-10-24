import sys,unittest
from pathlib import Path
from decimal import Decimal as D
from fractions import Fraction as F
sys.path[:0]=[str(Path(__file__).resolve().parents[1]/'src'/'engine')]
import spline_arc_core as core
import smooth_fit
import topology_certification as topo
import certified_curves as c

class SmoothTests(unittest.TestCase):
    def test_proven_disjoint_pairs_do_not_consume_narrow_phase_budget(self):
        curves=[[(c.I(3*i),c.I(0),c.I(1)),(c.I(3*i+1),c.I(0),c.I(1))] for i in range(100)]
        r=topo.all_pair_exclusions(curves,max_pairs=1)
        self.assertTrue(r['completed'])
        self.assertEqual(r['pairsTested'],0)
        self.assertEqual(r['broadPhasePairsExcluded'],4950)
        self.assertFalse(r['verified']) # Isolated lines remain open.
    def test_s_curve_has_no_added_kinks_and_retains_tolerance(self):
        r=core.convert_spline(3,[0]*4+[1]*4,[(0,0),(20,30),(40,-30),(60,0)])
        self.assertLessEqual(D(r['maxBoundMm']),D('.01'))
        self.assertLessEqual(r['maxAddedSmoothJointDegreesDiagnostic'],3e-6)
        self.assertLessEqual(D(r['maxEndpointBoundMm']),D('.0000001'))
    def test_closed_seam_and_internal_stitches_are_smooth(self):
        r=core.convert_spline(2,[0]*3+[F(1,4)]*2+[F(1,2)]*2+[F(3,4)]*2+[1]*3,[(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1),(0,-1),(1,-1),(1,0)],weights=[1,'0.7071067811865476']*4+[1],closed=True)
        self.assertTrue(r['closed'])
        self.assertLessEqual(r['maxAddedSmoothJointDegreesDiagnostic'],3e-6)
    def test_small_tolerance_merge_keeps_whole_biarc_units(self):
        # Merging only half a biarc moves an artificial correspondence endpoint
        # onto the source and breaks its untouched neighbor's tangent/position.
        r=core.convert_spline(3,[0]*4+[1]*4,[(0,0),(1,0),(2,1),(2,2)],options=core.Options(tolerance_mm=D('.001')))
        self.assertLessEqual(r['maxAddedSmoothJointDegreesDiagnostic'],3e-6)
        self.assertLessEqual(D(r['maxBoundMm']),D('.001'))
    def test_original_corner_is_preserved(self):
        r=core.convert_spline(3,[0]*4+[F(1,2)]*3+[1]*4,[(0,0),(1,0),(2,0),(3,0),(3,1),(3,2),(3,3)])
        self.assertTrue(any(q['intentionalCorner'] for q in r['jointChecks']))
        self.assertEqual(len(r['pieces']),2)

if __name__=='__main__':unittest.main()
