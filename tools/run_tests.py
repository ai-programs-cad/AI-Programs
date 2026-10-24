import sys,unittest
from pathlib import Path
root=Path(__file__).resolve().parents[1]
if sys.argv[1]=='cnc':
    base=root/'apps/cnc-file-doctor'
    sys.path[:0]=[str(base/'src'),str(base/'src/engine'),str(base/'tests'),str(base/'tests/legacy')]
    suite=unittest.defaultTestLoader.discover(str(base/'tests'),pattern='test_*.py')
elif sys.argv[1]=='partcraft':
    base=root/'apps/partcraft/Source/Cam';sys.path.insert(0,str(base))
    suite=unittest.defaultTestLoader.loadTestsFromNames(['test_pair_candidates','test_quality_v03','test_cam_stage','test_dxf_batch','test_regression_v01'])
else:raise SystemExit('Choose cnc or partcraft')
result=unittest.TextTestRunner(verbosity=1).run(suite)
raise SystemExit(not result.wasSuccessful())
