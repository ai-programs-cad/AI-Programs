import unittest, math, tempfile
from pathlib import Path
from fractions import Fraction as F
from decimal import Decimal as D
import certified_curves as c
import dxf_support as a

class IntervalTests(unittest.TestCase):
    def test_rational_enclosure(self):
        x=c.I(F(1,7));self.assertLessEqual(F(x.lo),F(1,7));self.assertGreaterEqual(F(x.hi),F(1,7))
    def test_negation_no_context_rounding(self):
        s='1.123456789012345678901234567890123456789';self.assertEqual((-c.I(s)).lo,D(s).copy_negate())
    def test_abs_no_context_rounding(self):
        s='1.123456789012345678901234567890123456789';self.assertEqual(c.I('-'+s).absmax,D(s))
    def test_division_zero_rejected(self):
        with self.assertRaises(ValueError):c.I(1)/c.I(-1,1)
    def test_pi_enclosure(self):
        p=c.pi_interval();self.assertLess(p.lo,D('3.141592653589793238462643383279502884197169399375105821'));self.assertGreater(p.hi,D('3.141592653589793238462643383279502884197169399375105820'))
    def test_trig_cardinal(self):
        for d,s,v in [(0,0,1),(90,1,0),(180,0,-1),(270,-1,0)]:
            si,co=c.trig(F(d));self.assertLessEqual(si.lo,s);self.assertGreaterEqual(si.hi,s);self.assertLessEqual(co.lo,v);self.assertGreaterEqual(co.hi,v)
    def test_trig_negative_period(self):
        s,cx=c.trig(F(-90));self.assertLessEqual(s.lo,-1);self.assertGreaterEqual(s.hi,-1)

class CurveTests(unittest.TestCase):
    def line(self):return [(F(0),F(0),F(1)),(F(1),F(0),F(1)),(F(2),F(0),F(1)),(F(3),F(0),F(1))]
    def test_exact_line(self):
        pieces=c.approximate(self.line(),(F(0),F(1)));self.assertEqual(len(pieces),1);self.assertEqual(pieces[0]['primitive']['type'],'LINE');self.assertEqual(D(pieces[0]['boundMm']),0)
    def test_displaced_line_rejected(self):
        g=dict(type='LINE',x0='0',x1='3',y0='.011',y1='.011');b,_,_=c.certificate(self.line(),g);self.assertGreater(b,D('.01'))
    def test_small_change_visible(self):
        g=dict(type='LINE',x0='0',x1='3',y0='.000001',y1='.000001');b,_,_=c.certificate(self.line(),g);self.assertGreaterEqual(b,D('.000001'))
    def test_rational_circle_quarter(self):
        # Rational Pythagorean arc from (1,0) to (-7/25,24/25), middle homogeneous (3/5,4/5,3/5).
        h=[(F(1),F(0),F(1)),(F(3,5),F(4,5),F(3,5)),(F(-7,25),F(24,25),F(1))]
        pieces=c.approximate(h,(F(0),F(1)))
        self.assertTrue(all(D(p['boundMm'])<=D('.01') for p in pieces));self.assertTrue(any(p['primitive']['type']=='ARC' for p in pieces))
    def test_clockwise_arc(self):
        h=[(F(1),F(0),F(1)),(F(3,5),F(-4,5),F(3,5)),(F(-7,25),F(-24,25),F(1))]
        ps=c.approximate(h,(F(0),F(1)));self.assertTrue(all(p['reverse'] for p in ps))
    def test_nonpositive_weights_rejected(self):
        with self.assertRaises(ValueError):c.bezier_spans(1,['0','0','1','1'],[('0','0'),('1','1')],['1','0'])
    def test_missing_weights_rejected(self):
        with self.assertRaises(ValueError):c.bezier_spans(1,['0','0','1','1'],[('0','0'),('1','1')],['1'])
    def test_unclamped_rejected(self):
        with self.assertRaises(ValueError):c.bezier_spans(1,['-1','0','1','2'],[('0','0'),('1','1')],['1','1'])
    def test_knot_insertion(self):
        # Quadratic B-spline, one interior knot; verify against independent de Boor recursion.
        u=list(map(F,[0,0,0,.5,1,1,1]));pts=[(F(i),F(i*i),F(1)) for i in range(4)]
        spans=c.bezier_spans(2,u,[(p[0],p[1]) for p in pts],[1]*4)
        def de_boor(t):
            k=max(i for i in range(2,4) if u[i]<=t);d=list(pts[k-2:k+1])
            for r in range(1,3):
                for j in range(2,r-1,-1):
                    i=k-2+j;alpha=(t-u[i])/(u[i+3-r]-u[i]);d[j]=tuple((1-alpha)*x+alpha*y for x,y in zip(d[j-1],d[j]))
            return d[2][:2]
        for left,right,h in spans:
            for t in [F(1,7),F(1,2),F(6,7)]:self.assertEqual(c.point(h,t),de_boor(left+(right-left)*t))
    def test_split_preserves_endpoints(self):
        h=self.line();l,r=c.split(h);self.assertEqual(c.point(l,F(1)),c.point(r));self.assertEqual(c.point(l),c.point(h));self.assertEqual(c.point(r,F(1)),c.point(h,F(1)))
    def test_radius_change_bound(self):
        g=dict(type='ARC',cx='0',cy='0',r='1',start='0',end='90');h=c.primitive_h(g)
        g2=dict(g,r='1.02');target=c.primitive_h(g2)
        # Disjoint endpoint distance alone disproves exact coincidence.
        self.assertGreater(a.distbound(tuple(p/pw for p,pw in [(h[0][0],h[0][2]),(h[0][1],h[0][2])]),tuple(p/pw for p,pw in [(target[0][0],target[0][2]),(target[0][1],target[0][2])])),D('.01'))

class TopologyTests(unittest.TestCase):
    def h(self,a0,a1):return c.primitive_h(dict(type='LINE',x0=str(a0[0]),y0=str(a0[1]),x1=str(a1[0]),y1=str(a1[1])))
    def test_separate_lines(self):self.assertTrue(a.separation(self.h((0,0),(1,0)),self.h((0,1),(1,1)))[0])
    def test_crossing_not_certified(self):self.assertFalse(a.separation(self.h((0,0),(1,1)),self.h((0,1),(1,0)),budget=1000)[0])
    def test_duplicate_not_certified(self):self.assertFalse(a.separation(self.h((0,0),(1,0)),self.h((0,0),(1,0)),budget=1000)[0])
    def test_shared_endpoint(self):self.assertTrue(a.separation(self.h((0,0),(1,0)),self.h((1,0),(1,1)),(c.I(1),c.I(0)))[0])
    def test_hole_moved_not_hidden(self):self.assertFalse(a.separation(self.h((0,0),(1,0)),self.h((.5,-1),(.5,1)),budget=1000)[0])
    def test_write_primitive_readback(self):
        row={'pairs':[(0,'SPLINE'),(5,'20'),(330,'1F'),(100,'AcDbEntity'),(8,'0'),(62,'7'),(100,'AcDbSpline')]}
        pieces=[{'primitive':dict(type='ARC',cx='1',cy='2',r='3',start='20',end='40')}]
        blob,n=a.replacement([],row,pieces,100);self.assertIn(b'AcDbArc',blob);self.assertNotIn(b'SPLINE',blob);self.assertIn(b'62\r\n7',blob)

if __name__=='__main__':unittest.main(verbosity=2)
