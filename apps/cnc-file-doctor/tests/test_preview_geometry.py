import sys,tempfile,unittest
from pathlib import Path
sys.path[:0]=[str(Path(__file__).resolve().parents[1]/'src'),str(Path(__file__).resolve().parents[1]/'src'/'engine'),str(Path(__file__).resolve().parent/'legacy')]
import preview_geometry as preview,doctor
from test_dxf_batch import fixture,spline
from test_closure import line

class PreviewTests(unittest.TestCase):
    def test_signed_bulge_semicircles_and_major_arcs(self):
        for bulge,side in ((1,-1),(-1,1),(2,-1),(-2,1)):
            with self.subTest(bulge=bulge):
                row={'pairs':[(0,'LWPOLYLINE'),(90,2),(70,0),(10,-1),(20,0),(42,str(bulge)),(10,1),(20,0)]}
                points,segments=preview.polyline_points(row)
                self.assertEqual(points[0],(-1.,0.));self.assertEqual(points[-1],(1.,0.))
                self.assertAlmostEqual(points[len(points)//2][1],side*abs(bulge),places=10)
                self.assertGreater(len(points),100)
    def test_closed_curved_polyline_is_not_reported_as_unsupported(self):
        row=[(0,'LWPOLYLINE'),(5,'100'),(330,'1F'),(8,'0'),(90,2),(70,1),(10,-1),(20,0),(42,1),(10,1),(20,0),(42,1)]
        result=self.render([row]);g=result['geometry'][0]
        self.assertFalse(result['diagnostics']['partial']);self.assertFalse(result['diagnostics']['markers']);self.assertEqual(len(g['points']),721)
        self.assertAlmostEqual(min(p[1] for p in g['points']),-1);self.assertAlmostEqual(max(p[1] for p in g['points']),1)
    def render(self,rows):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'part.dxf';p.write_bytes(fixture(rows));before=p.read_bytes();result=preview.build(p);self.assertEqual(p.read_bytes(),before);return result
    def test_gap_endpoints_are_located_and_bends_excluded(self):
        r=self.render([line('100',(0,0),(10,0)),line('101',(2,2),(3,3),'UP')]);markers=r['diagnostics']['markers']
        self.assertEqual(len([m for m in markers if m['code']=='OPEN_ENDPOINT']),2)
        self.assertEqual({tuple(m['point']) for m in markers},{(0.,0.),(10.,0.)})
    def test_zero_length_and_duplicate_locations(self):
        r=self.render([line('100',(1,2),(1,2)),line('101',(1,2),(1,2))]);codes={m['code'] for m in r['diagnostics']['markers']}
        self.assertIn('ZERO_LENGTH',codes);self.assertIn('DUPLICATE',codes)
    def test_spline_is_displayed_but_not_claimed_as_certificate(self):
        r=self.render([spline('100')]);g=r['geometry'][0]
        self.assertEqual(g['type'],'SPLINE');self.assertTrue(g['pending']);self.assertGreater(len(g['points']),16);self.assertTrue(r['diagnostics']['splinePreviewIsSampled'])
        self.assertAlmostEqual(g['points'][0][0],0);self.assertAlmostEqual(g['points'][-1][0],2)
    def test_truecolor_and_closed_contour(self):
        points=[(0,0),(10,0),(10,10),(0,10)];rows=[line(format(256+i,'X'),points[i],points[(i+1)%4])+[(420,0x22cc88)] for i in range(4)]
        r=self.render(rows);self.assertTrue(all(g['displayRgb']==0x22cc88 for g in r['geometry']));self.assertFalse(r['diagnostics']['markers']);self.assertTrue(r['diagnostics']['closureChecked'])
    def test_inspection_surfaces_open_contour_as_issue(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'part.dxf';p.write_bytes(fixture([line('100',(0,0),(10,0))]));self.assertEqual(doctor.scan(p)['status'],'ISSUES_FOUND')

if __name__=='__main__':unittest.main()
