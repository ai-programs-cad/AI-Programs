import unittest, tempfile, math
from pathlib import Path
from fractions import Fraction as F
from decimal import Decimal as D
import spline_arc_core as s
import spline_arc_batch as b
import certified_curves as c

class NURBSTests(unittest.TestCase):
    def cubic(self,points,weights=None,options=None):return s.convert_spline(3,[0]*4+[1]*4,points,weights,options=options)
    def check(self,r):
        self.assertLessEqual(D(r['maxBoundMm']),D('.01'));self.assertGreater(len(r['pieces']),0)
        self.assertTrue(all(p['primitive']['type'] in ('ARC','LINE') for p in r['pieces']))
        self.assertLessEqual(D(r['maxEndpointBoundMm']),D('.0000001'))
    def test_straight(self):
        r=self.cubic([(0,0),(1,0),(2,0),(3,0)]);self.check(r);self.assertEqual(len(r['pieces']),1);self.assertEqual(r['pieces'][0]['primitive']['type'],'LINE')
    def test_simple_curve(self):self.check(self.cubic([(0,0),(1,0),(2,1),(2,2)]))
    def test_s_curve_inflection(self):self.check(self.cubic([(0,0),(3,8),(7,-8),(10,0)]))
    def test_short_curve(self):self.check(self.cubic([(0,0),('0.00001',0),('0.00002','0.00001'),('0.00002','0.00002')]))
    def test_unequal_positive_weights(self):self.check(self.cubic([(0,0),(1,3),(4,-1),(5,2)],[1,F(1,2),3,1]))
    def test_closed_clamped(self):
        r=s.convert_spline(3,[0]*4+[F(1,2)]*3+[1]*4,[(0,0),(10,0),(10,10),(0,10),(-10,10),(-10,0),(0,0)],closed=True);self.check(r);self.assertTrue(r['closed'])
    def test_periodic_unclamped(self):
        r=s.convert_spline(2,list(range(-2,7)),[(1,0),(0,1),(-1,0),(0,-1),(1,0),(0,1)],closed=True,periodic=True);self.check(r);self.assertTrue(r['periodic'])
    def test_unclamped_matches_independent_deboor(self):
        u=list(map(F,range(-2,7)));pts=[(F(x),F(y)) for x,y in [(1,0),(0,1),(-1,0),(0,-1),(1,0),(0,1)]]
        spans=s.spans(2,u,pts,closed=True,periodic=True)
        for seg in spans:
            a,z=seg['domain']
            for v in (F(1,7),F(1,2),F(6,7)):
                t=a+(z-a)*v;k=max(i for i in range(2,len(pts)) if u[i]<=t);q=[p for p in pts[k-2:k+1]]
                for r in (1,2):
                    for j in range(2,r-1,-1):
                        i=k-2+j;alpha=(t-u[i])/(u[i+3-r]-u[i]);q[j]=tuple((1-alpha)*x+alpha*y for x,y in zip(q[j-1],q[j]))
                self.assertEqual(c.point(seg['h'],v),q[2])
    def test_closed_flag_mismatch(self):
        with self.assertRaises(s.ReviewRequired):s.spans(1,[0,0,1,1],[(0,0),(1,1)],closed=True)
    def test_negative_weight(self):
        with self.assertRaises(s.ReviewRequired):self.cubic([(0,0),(1,0),(2,1),(2,2)],[1,-1,1,1])
    def test_zero_weight(self):
        with self.assertRaises(s.ReviewRequired):self.cubic([(0,0),(1,0),(2,1),(2,2)],[1,0,1,1])
    def test_zero_length(self):
        with self.assertRaises(s.ReviewRequired):self.cubic([(1,2)]*4)
    def test_degree_five(self):
        self.check(s.convert_spline(5,[0]*6+[1]*6,[(0,0),(1,0),(2,1),(3,2),(4,2),(5,1)]))
    def test_knots_nonmonotone(self):
        with self.assertRaises(s.ReviewRequired):s.spans(1,[0,1,0,1],[(0,0),(1,1)])
    def test_candidate_limit(self):
        with self.assertRaises(s.ResourceLimit):self.cubic([(0,0),(3,8),(7,-8),(10,0)],options=s.Options(max_candidates=1))
    def test_segment_limit(self):
        with self.assertRaises(s.ResourceLimit):self.cubic([(0,0),(3,8),(7,-8),(10,0)],options=s.Options(max_segments=1))
    def test_depth_limit(self):
        with self.assertRaises(s.ResourceLimit):self.cubic([(0,0),(3,8),(7,-8),(10,0)],options=s.Options(max_depth=1))
    def test_time_limit(self):
        with self.assertRaises(s.ResourceLimit):self.cubic([(0,0),(3,8),(7,-8),(10,0)],options=s.Options(max_seconds=1e-12))
    def test_tolerance_validation(self):
        for x in ('0','-1','NaN','Infinity'):
            with self.assertRaises(ValueError):s.Options(tolerance_mm=D(x)).validate()
    def test_adjacent_merge_never_worsens_tolerance(self):
        points=[(0,0),(1,0),(2,1),(2,2)];r=self.cubic(points);raw=self.cubic(points,options=s.Options(merge=False));self.check(r);self.assertLessEqual(len(r['pieces']),len(raw['pieces']))
    def test_source_controls_unchanged(self):
        points=[(0,0),(1,0),(2,1),(2,2)];copy=list(points);self.cubic(points);self.assertEqual(points,copy)
    def test_global_minimum_not_falsely_claimed(self):self.assertFalse(self.cubic([(0,0),(1,0),(2,0),(3,0)])['minimumElementCountProven'])
    def test_length_encloses_straight(self):
        r=self.cubic([(0,0),(1,0),(2,0),(3,0)]);self.assertLessEqual(D(r['sourceLength']['lowerBoundMm']),3);self.assertGreaterEqual(D(r['sourceLength']['upperBoundMm']),3)
    def test_serialization(self):
        r=self.cubic([(0,0),(1,0),(2,0),(3,0)]);rr=s.serializable(r);self.assertIsInstance(rr['pieces'][0]['segments'][0]['h'][0][0],str)
    def test_curve_not_approximated_by_straight_polygon(self):
        r=self.cubic([(0,0),(1,0),(2,1),(2,2)]);self.assertGreater(sum(p['primitive']['type']=='ARC' for p in r['pieces']),0)
    def test_finite_restriction(self):
        h=[(F(0),F(0),F(1)),(F(2),F(0),F(1))];v=s.restrict(h,F(1,4),F(3,4));self.assertEqual(c.point(v),(F(1,2),F(0)));self.assertEqual(c.point(v,F(1)),(F(3,2),F(0)))
    def test_stationary_endpoint_tangent(self):
        self.check(self.cubic([(0,0),(0,0),(1,1),(2,2)]))
    def test_arbitrary_rotation_and_translation(self):
        angle=math.radians(31.7)
        points=[(1000+math.cos(angle)*x-math.sin(angle)*y,-321+math.sin(angle)*x+math.cos(angle)*y) for x,y in [(0,0),(1,0),(2,1),(2,2)]]
        self.check(self.cubic(points))
    def test_nonunit_knot_domain(self):
        self.check(s.convert_spline(3,['100']*4+['100.001']*4,[(0,0),(1,0),(2,1),(2,2)]))
    def test_periodic_unequal_weights(self):
        self.check(s.convert_spline(2,list(range(-2,7)),[(1,0),(0,1),(-1,0),(0,-1),(1,0),(0,1)],[1,F(1,2),2,3,1,F(1,2)],closed=True,periodic=True))
    def test_rational_quarter_circle(self):
        self.check(s.convert_spline(2,[0,0,0,1,1,1],[(1,0),(1,1),(0,1)],['1','0.7071067811865476','1']))
    def test_degree_one_nonuniform_speed(self):
        r=s.convert_spline(1,[0,0,1,1],[(0,0),(1,1)],[1,2]);self.check(r);self.assertEqual(len(r['pieces']),1);self.assertEqual(r['pieces'][0]['certificateKind'],'COLLINEAR_WHOLE_IMAGE')
    def test_collinear_nonuniform_cubic_one_line(self):
        r=self.cubic([(0,0),(F(1,10),0),(F(1,3),0),(3,0)],[1,2,4,1]);self.check(r);self.assertEqual(len(r['pieces']),1)

class BatchDiscoveryTests(unittest.TestCase):
    def test_exact_and_nonexact_names_do_not_collide(self):
        e,c=set(),set();a=b.output_names(Path('sample.dxf'),e,c);z=b.output_names(Path('sample-EXACT.dxf'),e,c);self.assertNotEqual(a[1],z[1])
    def test_identical_basenames_in_different_folders(self):
        e,c=set(),set();a=b.output_names(Path('first/x.dxf'),e,c);z=b.output_names(Path('second/x.dxf'),e,c);self.assertNotEqual(a,z)
    def test_arbitrary_file_count_names_and_nested(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);(p/'nested').mkdir()
            for i in range(13):(p/'nested'/(str(i)+' русский.DXF')).write_text('x')
            self.assertEqual(len(b.discover([str(p)])),13)
    def test_deduplicates_same_source(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'whatever.dxf';p.write_text('x');self.assertEqual(len(b.discover([str(p),str(p)])),1)
    def test_missing_source(self):
        with self.assertRaises(ValueError):b.discover(['missing-nonexistent.dxf'])
    def test_fit_points_only_rejected(self):
        row={'pairs':[(0,'SPLINE'),(70,'8'),(71,'3'),(72,'0'),(73,'0')]}
        with self.assertRaises(s.ReviewRequired):s.convert_spline(**b.source_data(row))
    def test_xdata_not_silently_dropped(self):
        row={'pairs':[(0,'SPLINE'),(70,'8'),(71,'1'),(72,'4'),(73,'2'),(10,'0'),(20,'0'),(10,'1'),(20,'1'),(40,'0'),(40,'0'),(40,'1'),(40,'1'),(1001,'APP')]}
        with self.assertRaises(s.ReviewRequired):b.source_data(row)

if __name__=='__main__':unittest.main(verbosity=2)
