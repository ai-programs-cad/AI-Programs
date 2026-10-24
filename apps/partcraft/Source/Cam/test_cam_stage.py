import tempfile, unittest
from pathlib import Path
from unittest.mock import patch
import prepare_cam_arcs as stage
import dxf_support as dx

def fixture(active=True):
    pairs=[(0,'SECTION'),(2,'TABLES'),(0,'VPORT'),(2,'*ACTIVE' if active else 'Other'),(72,'1000'),(0,'ENDSEC'),(0,'SECTION'),(2,'ENTITIES'),(0,'LINE'),(5,'1'),(8,'UP'),(62,'5'),(420,'255'),(10,'1.23456789'),(20,'0'),(11,'2'),(21,'0'),(0,'ENDSEC'),(0,'EOF')]
    return ''.join(f'{c}\r\n{v}\r\n' for c,v in pairs).encode()

class CamStageTests(unittest.TestCase):
    def test_display_modifies_only_vport_circle_sides(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'x.dxf';p.write_bytes(fixture());before=p.read_bytes()
            r=stage.display_resolution(p)
            self.assertEqual(p.read_bytes(),before.replace(b'72\r\n1000',b'72\r\n20000'))
            self.assertTrue(r['entityBytesUnchanged']);self.assertFalse(r['regenallExecuted'])
    def test_no_active_viewport_stops_without_modifying_file(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'x.dxf';p.write_bytes(fixture(False))
            with self.assertRaises(ValueError):stage.display_resolution(p)
            self.assertEqual(p.read_bytes(),fixture(False))
    def test_active_viewport_symbol_is_case_insensitive(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'x';p.write_bytes(fixture().replace(b'*ACTIVE',b'*Active'))
            self.assertEqual(stage.display_resolution(p)['viewres'],20000)
    def test_bend_truecolor_payload_preserved(self):
        with tempfile.TemporaryDirectory() as d:
            a,b=Path(d)/'a',Path(d)/'b';a.write_bytes(fixture());b.write_bytes(fixture())
            self.assertTrue(stage.preserved_bends(a,b)['byteIdentical'])
            b.write_bytes(fixture().replace(b'420\r\n255',b'420\r\n256'))
            self.assertFalse(stage.preserved_bends(a,b)['byteIdentical'])
    def test_default_tolerance_is_ten_microns(self):
        import inspect
        self.assertEqual(inspect.signature(stage.run).parameters['tolerance'].default,'0.01')
    def test_existing_output_is_not_overwritten(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'a.dxf';p.write_bytes(fixture());o=Path(d)/'out';o.mkdir()
            with self.assertRaises(FileExistsError):stage.run([str(p)],o)
            self.assertEqual(p.read_bytes(),fixture())
    def test_no_recursive_output_inside_input(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d)/'a.dxf').write_bytes(fixture())
            with self.assertRaises(ValueError):stage.run([d],Path(d)/'out')

if __name__=='__main__':unittest.main()
