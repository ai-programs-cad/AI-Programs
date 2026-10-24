import tempfile, unittest
from pathlib import Path
from fractions import Fraction as F
import spline_arc_batch as b
import spline_arc_core as s
import dxf_support as dx

def spline(handle,points=None,degree=3,knots=None,weights=None,flags=8,layer='0'):
    points=points or [(0,0),(1,0),(2,1),(2,2)]
    knots=knots or [0]*(degree+1)+[1]*(degree+1)
    row=[(0,'SPLINE'),(5,handle),(330,'1F'),(100,'AcDbEntity'),(8,layer),(62,5 if layer=='UP' else 7),(420,255),(6,'Continuous'),(100,'AcDbSpline'),(210,0),(220,0),(230,1),(70,flags),(71,degree),(72,len(knots)),(73,len(points)),(74,0)]
    row += [(40,x) for x in knots]
    if weights is not None:row += [(41,x) for x in weights]
    for x,y in points:row += [(10,x),(20,y),(30,0)]
    return row

def fixture(entities):
    tags=[(0,'SECTION'),(2,'HEADER'),(9,'$ACADVER'),(1,'AC1032'),(9,'$INSUNITS'),(70,4),(9,'$HANDSEED'),(5,'FFFF'),(0,'ENDSEC'),(0,'SECTION'),(2,'TABLES'),(0,'TABLE'),(2,'BLOCK_RECORD'),(0,'BLOCK_RECORD'),(5,'1F'),(2,'*Model_Space'),(340,'A1'),(0,'ENDTAB'),(0,'TABLE'),(2,'LTYPE'),(0,'LTYPE'),(5,'10'),(2,'Continuous'),(3,'Solid'),(40,0),(73,0),(0,'ENDTAB'),(0,'TABLE'),(2,'LAYER')]
    for i,(name,color) in enumerate([('0',7),('UP',5),('DOWN',1)]):tags += [(0,'LAYER'),(5,format(40+i,'X')),(2,name),(62,color),(6,'Continuous')]
    tags += [(0,'ENDTAB'),(0,'ENDSEC'),(0,'SECTION'),(2,'BLOCKS'),(0,'BLOCK'),(5,'30'),(330,'1F'),(2,'*Model_Space'),(10,0),(20,0),(30,0),(0,'ENDBLK'),(5,'31'),(330,'1F'),(0,'ENDSEC'),(0,'SECTION'),(2,'ENTITIES')]
    for e in entities:tags+=e
    tags += [(0,'ENDSEC'),(0,'SECTION'),(2,'OBJECTS'),(0,'LAYOUT'),(5,'A1'),(100,'AcDbLayout'),(1,'Model'),(330,'1F'),(0,'ENDSEC'),(0,'EOF')]
    return dx.encode(tags)

class FileTests(unittest.TestCase):
    def run_file(self,entities,options=None,convert_bends=False):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);src=p/'произвольная деталь.dxf';exact=p/'exact.dxf';cnc=p/'cnc.dxf';src.write_bytes(fixture(entities));before=src.read_bytes()
            r=b.process_file(src,exact,cnc,options or s.Options(),convert_bends=convert_bends)
            self.assertEqual(src.read_bytes(),before);self.assertEqual(exact.read_bytes(),before)
            data=cnc.read_bytes() if cnc.exists() else b''
            return r,data
    def test_twenty_splines_no_fixed_count(self):
        rows=[spline(format(256+i,'X'),[(i*10,0),(i*10+1,0),(i*10+2,0),(i*10+3,0)]) for i in range(20)]
        r,data=self.run_file(rows);self.assertEqual(len(r['splines']),20);self.assertEqual(r['allSplineCountRemaining'],0);self.assertTrue(r['geometryBoundWithinTolerance']);self.assertNotIn(b'SPLINE',data)
    def test_original_analytic_primitives_preserved(self):
        line=[(0,'LINE'),(5,'B1'),(330,'1F'),(100,'AcDbEntity'),(8,'DOWN'),(100,'AcDbLine'),(10,20),(20,0),(30,0),(11,20),(21,2),(31,0)]
        circle=[(0,'CIRCLE'),(5,'B2'),(330,'1F'),(100,'AcDbEntity'),(8,'0'),(100,'AcDbCircle'),(10,20),(20,20),(30,0),(40,2)]
        arc=[(0,'ARC'),(5,'B3'),(330,'1F'),(100,'AcDbEntity'),(8,'UP'),(100,'AcDbCircle'),(10,30),(20,30),(30,0),(40,2),(100,'AcDbArc'),(50,0),(51,90)]
        r,data=self.run_file([spline('100'),line,circle,arc]);preserved={p['handle'] for p in r['unchangedEntityRecords']};self.assertTrue({'B1','B2','B3'}<=preserved)
    def test_mixed_failure_does_not_stop_other_spline(self):
        r,data=self.run_file([spline('100',weights=[1,0,1,1]),spline('101',points=[(5,0),(6,0),(7,1),(7,2)])]);self.assertIn('error',r['splines'][0]);self.assertTrue(r['splines'][1]['deviationBoundVerified']);self.assertEqual(r['status'],'NEEDS_REVIEW');self.assertFalse(r['productionReady'])
    def test_bend_spline_preserved_without_role_evidence(self):
        r,data=self.run_file([spline('100',layer='UP')]);self.assertEqual(r['splines'][0]['role'],'BEND');self.assertIn('error',r['splines'][0]);self.assertEqual(r['allSplineCountRemaining'],1)
    def test_bend_conversion_opt_in_not_assignment_certificate(self):
        r,data=self.run_file([spline('100',layer='UP')],convert_bends=True);self.assertEqual(r['allSplineCountRemaining'],0);self.assertFalse(r['splines'][0]['assignmentVerified']);self.assertFalse(r['productionReady'])
    def test_attributes_truecolor_and_layer(self):
        r,data=self.run_file([spline('100')]);self.assertEqual(r['splines'][0]['attributes']['420'],'255');self.assertEqual(r['splines'][0]['attributes']['8'],'0');self.assertIn('writtenCurveVerification',r['splines'][0])
    def test_closed_periodic_in_file(self):
        row=spline('100',[(1,0),(0,1),(-1,0),(0,-1),(1,0),(0,1)],2,list(range(-2,7)),flags=11)
        r,data=self.run_file([row]);self.assertEqual(r['allSplineCountRemaining'],0);self.assertTrue(r['outputTopology']['closedDegreeTwo']);self.assertTrue(r['componentCorrespondencePreserved'])
    def test_limited_conversion_never_passes(self):
        r,data=self.run_file([spline('100')],s.Options(max_segments=1));self.assertIn('error',r['splines'][0]);self.assertEqual(r['status'],'NEEDS_REVIEW')
    def test_open_cut_contour_not_certified(self):
        r,data=self.run_file([spline('100')]);self.assertFalse(r['outputTopology']['closedDegreeTwo']);self.assertFalse(r['productionReady'])
    def test_output_is_real_arc_not_polyline(self):
        r,data=self.run_file([spline('100')]);self.assertIn(b'AcDbArc',data);self.assertNotIn(b'LWPOLYLINE',data)
    def test_repeated_handles_allocated_uniquely(self):
        r,data=self.run_file([spline('100'),spline('101',points=[(10,0),(11,0),(12,1),(12,2)])]);handles=[p['outputHandle'] for o in r['splines'] for p in o['pieces']];self.assertEqual(len(handles),len(set(handles)))
    def test_non_mm_is_not_guessed(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);src=p/'in.dxf';src.write_bytes(fixture([spline('100')]).replace(b'$INSUNITS\r\n70\r\n4',b'$INSUNITS\r\n70\r\n1'))
            r=b.process_file(src,p/'exact.dxf',p/'cnc.dxf',s.Options());self.assertTrue(r['fileErrors']);self.assertFalse(r['productionReady'])

if __name__=='__main__':unittest.main(verbosity=2)
