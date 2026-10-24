import random,unittest
from decimal import Decimal as D
from fractions import Fraction as F
import curve_pair_candidates as broad
import certified_curves as c
import dxf_support as dx
class BroadTests(unittest.TestCase):
 def test_exact_same_candidate_set_as_exhaustive(self):
  rng=random.Random(20261022)
  boxes=[]
  for i in range(500):
   x,y=D(rng.randrange(-100,100)),D(rng.randrange(-100,100));boxes.append((x,x+D(rng.randrange(0,15)),y,y+D(rng.randrange(0,15))))
  expected={(i,j) for i in range(len(boxes)) for j in range(i+1,len(boxes)) if not dx.disjoint(boxes[i],boxes[j])}
  actual=list(broad.candidate_pairs(boxes));self.assertEqual(len(actual),len(set(actual)));self.assertEqual(expected,set(actual))
 def test_touch_and_zero_width_boxes_not_dropped(self):
  boxes=[tuple(map(D,x)) for x in ((0,1,0,1),(1,2,1,2),(1,1,-1,3),(3,4,3,4))]
  self.assertEqual(set(broad.candidate_pairs(boxes)),{(0,1),(0,2),(1,2)})
 def test_invalid_rational_weight_rejected(self):
  with self.assertRaises(ValueError):broad.curve_box([(c.I(0),c.I(0),c.I(-1))])
if __name__=='__main__':unittest.main()
