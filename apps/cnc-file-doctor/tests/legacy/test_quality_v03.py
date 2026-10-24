import unittest,tempfile
from pathlib import Path
from fractions import Fraction as F
from decimal import Decimal as D
import certified_curves as c
import spline_arc_core as core
import spline_arc_batch as batch
import biarc_refinement as bi
import topology_certification as topo
import junction_checks as junction
import dxf_support as dx
import dxf_audit,native_autocad_audit as native
from test_dxf_batch import fixture,spline

def line(handle,a,b):return {'pairs':[(0,'LINE'),(5,handle),(330,'1F'),(100,'AcDbEntity'),(8,'0'),(10,str(a[0])),(20,str(a[1])),(30,'0'),(11,str(b[0])),(21,str(b[1])),(31,'0')]}
def square(prefix,x0,y0,x1,y1):
    pts=[(x0,y0),(x1,y0),(x1,y1),(x0,y1)]
    return [line(prefix+str(i),a,b) for i,(a,b) in enumerate(zip(pts,pts[1:]+pts[:1]))]
def curves(rows):return [h for r in rows for h in topo.row_curves(r,True)]

class TopologyTests(unittest.TestCase):
    def test_one_hole(self):
        r=topo.hierarchy([curves(square('A',0,0,10,10)),curves(square('B',2,2,4,4))]);self.assertEqual(r['holeCount'],1);self.assertEqual(r['depths'],[0,1]);self.assertEqual(r['parents'],[None,0])
    def test_two_holes_and_island(self):
        r=topo.hierarchy([curves(square('A',0,0,20,20)),curves(square('B',2,2,5,5)),curves(square('C',8,8,9,9)),curves(square('D',3,3,4,4))]);self.assertEqual(r['holeCount'],2);self.assertEqual(r['depths'],[0,1,1,2])
    def test_hole_moved_outside_detected(self):
        s=square('A',0,0,10,10)+square('B',2,2,4,4);o=square('A',0,0,10,10)+square('B',12,2,14,4)
        parent={dx.one(r,5):dx.one(r,5) for r in o};r=topo.compare(s,o,parent,'.01');self.assertFalse(r['nestingPreserved']);self.assertFalse(r['holeCountPreserved']);self.assertFalse(r['verifiedAtJoinTolerance'])
    def test_displacement_not_proven_by_nesting_alone(self):
        s=square('A',0,0,10,10)+square('B',2,2,4,4);parent={dx.one(r,5):dx.one(r,5) for r in s};r=topo.compare(s,s,parent,'.01');self.assertFalse(r['relativePlacementPreservedWithinGeometricBound'])
    def test_missing_hole_detected(self):
        s=square('A',0,0,10,10)+square('B',2,2,4,4);o=square('A',0,0,10,10);r=topo.compare(s,o,{dx.one(x,5):dx.one(x,5) for x in o},'.01');self.assertFalse(r['verifiedAtJoinTolerance'])
    def test_vertex_ray_fallback(self):
        w,proof=topo.winding(curves(square('A',2,2,4,4)),(c.I(0),c.I(2)));self.assertEqual(w,0);self.assertNotEqual(proof['ray'][1],'0');self.assertTrue(proof['endpointJoinZonesDoNotCrossRay'])
    def test_reversed_loop_winding(self):
        hs=curves(square('A',0,0,10,10));a,_=topo.winding(hs,(c.I(5),c.I(5)));b,_=topo.winding([list(reversed(h)) for h in hs[::-1]],(c.I(5),c.I(5)));self.assertEqual(a,-b)
    def test_bowtie_intersection_not_pass(self):
        pts=[(0,0),(2,2),(0,2),(2,0)];rows=[line(str(i),a,b) for i,(a,b) in enumerate(zip(pts,pts[1:]+pts[:1]))];r=topo.all_pair_exclusions(curves(rows));self.assertFalse(r['verified']);self.assertTrue(r['unresolved'])
    def test_duplicate_reversed_line(self):
        r=topo.duplicates([line('A',(0,0),(1,1)),line('B',(1,1),(0,0))]);self.assertEqual(r,[['A','B']])
    def test_partial_overlap_not_pass(self):
        r=topo.all_pair_exclusions(curves([line('A',(0,0),(2,0)),line('B',(1,0),(3,0))]));self.assertFalse(r['verified'])
    def test_circular_hole(self):
        r={'pairs':[(0,'CIRCLE'),(5,'C'),(8,'0'),(10,'5'),(20,'5'),(40,'1')]};h=topo.hierarchy([curves(square('A',0,0,10,10)),curves([r])]);self.assertEqual(h['holeCount'],1)
    def test_monotonic_source_injectivity(self):self.assertTrue(topo.monotone_projection([(F(0),F(0),F(1)),(F(1),F(2),F(1)),(F(3),F(0),F(1))]))

class QualityTests(unittest.TestCase):
    def test_biarc_reduces_smooth_curve(self):
        data=dict(degree=3,knots=[0]*4+[1]*4,points=[(0,0),(1,0),(2,1),(2,2)])
        old=core._convert_v02(**data);new=core.convert_spline(**data)
        self.assertLess(len(new['pieces']),len(old['pieces']));self.assertLessEqual(D(new['maxBoundMm']),D('.01'));self.assertLessEqual(D(new['maxEndpointBoundMm']),D('.0000001'));self.assertLessEqual(new['maxAddedSmoothJointDegreesDiagnostic'],old['maxJointTangentErrorDegreesDiagnostic']+1e-5)
    def test_tighter_tolerance_kept(self):
        r=core.convert_spline(3,[0]*4+[1]*4,[(0,0),(1,0),(2,1),(2,2)],options=core.Options(tolerance_mm=D('.001')));self.assertLessEqual(D(r['maxBoundMm']),D('.001'))
    def test_intentional_knot_corner_not_smoothed(self):
        data=dict(degree=3,knots=[0]*4+[F(1,2)]*3+[1]*4,points=[(0,0),(1,0),(2,0),(3,0),(3,1),(3,2),(3,3)])
        r=core.convert_spline(**data);j=r['jointChecks'];self.assertTrue(any(t['intentionalCorner'] for t in j));self.assertTrue(all(abs(t['sourceAngleDegreesDiagnostic']-t['outputAngleDegreesDiagnostic'])<.00001 for t in j))
    def test_interval_angle_small(self):
        a=(c.I(1),c.I(0));s,co=c.trig(F(1,1000000));b=(co,s);bound=junction.small_angle_bound(a,b);self.assertGreaterEqual(bound,D('.000001'));self.assertLess(bound,D('.000002'))
    def test_real_corner_retained(self):
        rows=square('A',0,0,10,10);r=junction.check(rows,rows,[]);self.assertTrue(r['originalCornersPreservedWithinAngularTolerance']);self.assertEqual(len(r['junctions']),4);self.assertTrue(all(abs(j['sourceAngleDegreesDiagnostic']-90)<1e-10 for j in r['junctions']))

class AuditTests(unittest.TestCase):
    def test_native_script_is_no_fix_no_join(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'audit.scr';native.make_script(p,Path(d)/'entities.txt');s=p.read_text();self.assertIn('"_.AUDIT" "_N"',s)
            for command in ('_.JOIN','_.OVERKILL','_.RECOVER','_.SAVE','_.QSAVE'):self.assertNotIn(command,s)
    def test_structural_audit(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.dxf';p.write_bytes(fixture([line('100',(0,0),(1,1))['pairs']]));r=dxf_audit.audit(p);self.assertEqual(r['status'],'STRUCTURAL_AUDIT_PASS');self.assertTrue(r['onlyArcLineCircle']);self.assertTrue(r['modelSpaceOnly']);self.assertTrue(r['unitsMm'])
    def test_native_coordinate_mismatch_detected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.dxf';p.write_bytes(fixture([line('100',(0,0),(1,1))['pairs']]));text='GEOMETRY|100|LINE|0|0|0|0|2|2|0|NA|NA|NA|0|0|1';self.assertFalse(native.coordinate_check(text,p)['verified'])
    def test_native_coordinate_match(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.dxf';p.write_bytes(fixture([line('100',(0,0),(1,1))['pairs']]));text='GEOMETRY|100|LINE|0|0|0|0|1|1|0|NA|NA|NA|0|0|1';self.assertTrue(native.coordinate_check(text,p)['verified'])
    def test_native_large_radius_angular_error_scaled(self):
        with tempfile.TemporaryDirectory() as d:
            row=[(0,'ARC'),(5,'100'),(330,'1F'),(100,'AcDbEntity'),(8,'0'),(100,'AcDbCircle'),(10,0),(20,0),(30,0),(40,'100000000000'),(100,'AcDbArc'),(50,0),(51,1)]
            p=Path(d)/'a.dxf';p.write_bytes(fixture([row]));text='GEOMETRY|100|ARC|0|0|0|0|NA|NA|NA|100000000000|0|1.00000001|0|0|1';self.assertFalse(native.coordinate_check(text,p)['verified'])
    def test_native_nonstandard_normal_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.dxf';p.write_bytes(fixture([line('100',(0,0),(1,1))['pairs']]));text='GEOMETRY|100|LINE|0|0|0|0|1|1|0|NA|NA|NA|0|0|-1';self.assertFalse(native.coordinate_check(text,p)['verified'])

if __name__=='__main__':unittest.main(verbosity=2)
