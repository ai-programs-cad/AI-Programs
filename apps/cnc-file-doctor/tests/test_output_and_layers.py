import sys,tempfile,unittest
from pathlib import Path
sys.path[:0]=[str(Path(__file__).resolve().parents[1]/'src'),str(Path(__file__).resolve().parents[1]/'src'/'engine'),str(Path(__file__).resolve().parent/'legacy')]
import doctor,layer_split,dxf_support as dx
from test_dxf_batch import fixture
from test_closure import line

def drawing(rows):
    return fixture(rows).replace(dx.encode([(2,'LAYER')]),dx.encode([(2,'LAYER'),(70,3)]),1)

class OutputTests(unittest.TestCase):
    def test_numbered_inside_source_and_skip_previous_results(self):
        with tempfile.TemporaryDirectory() as t:
            parent=Path(t)/'Parts';parent.mkdir();source=parent/'one.dxf';source.write_bytes(drawing([line('100',(0,0),(1,0))]));(parent/'Parts_1').mkdir()
            a=doctor.run_auto_job([parent]);self.assertEqual(a['outputFolders'],[str(parent/'Parts_2')]);self.assertEqual(doctor.discover_inputs([parent]),[source])
            b=doctor.run_auto_job([parent]);self.assertEqual(b['outputFolders'],[str(parent/'Parts_3')]);self.assertEqual(len(b['files']),1)
            self.assertEqual(b['files'][0]['processedFolder'],str(parent/'Parts_3'));self.assertTrue(Path(b['files'][0]['reportPath']).exists())
    def test_multiple_sources_get_separate_folders(self):
        with tempfile.TemporaryDirectory() as t:
            inputs=[]
            for name in ('A','B'):
                p=Path(t)/name;p.mkdir();source=p/'one.dxf';source.write_bytes(drawing([line('100',(0,0),(1,0))]));inputs.append(source)
            job=doctor.run_auto_job(inputs);self.assertEqual(len(job['outputFolders']),2)
            for f in job['files']:self.assertEqual(Path(f['processedFolder']).parent,Path(f['source']).parent)
    def test_ready_dxf_is_directly_in_numbered_folder(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t)/'Input';p.mkdir();source=p/'square.dxf'
            pts=[(0,0),(10,0),(10,10),(0,10)];source.write_bytes(drawing([line(format(256+i,'X'),pts[i],pts[(i+1)%4]) for i in range(4)]))
            job=doctor.run_auto_job([source],True);f=job['files'][0]
            self.assertEqual(Path(f['candidate']).parent,p/'Input_1');self.assertTrue(Path(f['candidate']).is_file());self.assertEqual(doctor.discover_inputs([p]),[source])
            self.assertEqual(doctor.discover_inputs([p/'Input_1']),[Path(f['candidate'])])

class LayerTests(unittest.TestCase):
    def test_dash_pattern_aliases_ignore_names_and_optional_zero_flags(self):
        a=[(0,'LTYPE'),(2,'DASH_A'),(3,'First'),(40,'1.00'),(72,'65'),(49,'0.50'),(74,'0'),(49,'-0.50'),(74,'0')]
        b=[(0,'LTYPE'),(2,'DASH_B'),(3,'Second'),(40,'1'),(49,'.5'),(49,'-.5')]
        self.assertEqual(layer_split.pattern_key(a),layer_split.pattern_key(b))
    def test_assignment_origin_and_continuous_alias_do_not_split_groups(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf'
            rows=[line('100',(0,0),(1,0)),line('101',(0,1),(1,1))+[(6,'ByBlock'),(62,0),(370,-2)],line('102',(0,2),(1,2))+[(6,'SLD-Solid'),(62,7),(370,-3)]]
            end=dx.encode([(0,'ENDTAB'),(0,'TABLE'),(2,'LAYER')])
            alias=dx.encode([(0,'LTYPE'),(5,'11'),(2,'SLD-Solid'),(3,'Alias solid'),(40,'0.0'),(73,0)])
            source.write_bytes(drawing(rows).replace(end,alias+end,1))
            result=layer_split.split(source,p/'o.dxf',('color','linetype','lineweight'));self.assertEqual(result['createdLayerCount'],1)
            out=dx.read(p/'o.dxf')[1];self.assertEqual(len({dx.one(r,8) for r in out}),1);self.assertEqual(dx.one(out[1],6),'ByBlock')
    def test_explicit_rgb_matches_effective_indexed_red(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf';source.write_bytes(drawing([line('100',(0,0),(1,0))+[(62,1)],line('101',(0,1),(1,1))+[(420,16711680)]]))
            result=layer_split.split(source,p/'o.dxf',('color',));self.assertEqual(result['createdLayerCount'],1)
    def test_recorded_default_weight_matches_explicit_weight(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf';raw=drawing([line('100',(0,0),(1,0)),line('101',(0,1),(1,1))+[(370,30)]])
            raw=raw.replace(dx.encode([(9,'$HANDSEED')]),dx.encode([(9,'$LWDEFAULT'),(370,30),(9,'$HANDSEED')]),1);source.write_bytes(raw)
            result=layer_split.split(source,p/'o.dxf',('lineweight',));self.assertEqual(result['createdLayerCount'],1)
    def test_inherited_color_and_truecolor_grouping_preserve_geometry(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'input.dxf';target=p/'split.dxf'
            rows=[line('100',(0,0),(10,0)),line('101',(0,1),(10,1))+[(62,7)],line('102',(0,2),(10,2))+[(420,255)]]
            source.write_bytes(drawing(rows));before=source.read_bytes();r=layer_split.split(source,target,('color',))
            self.assertEqual(r['createdLayerCount'],2);self.assertTrue(r['geometryBytesUnchanged']);self.assertEqual(source.read_bytes(),before)
            pairs,out=dx.read(target);self.assertEqual(dx.one(out[0],8),dx.one(out[1],8));self.assertNotEqual(dx.one(out[0],8),dx.one(out[2],8));self.assertEqual(dx.one(out[2],420),'255')
    def test_linetype_weight_and_type_can_be_selected(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf';source.write_bytes(drawing([line('100',(0,0),(1,0)),line('101',(0,1),(1,1))+[(370,50)]]))
            r=layer_split.split(source,p/'a.dxf',('lineweight',));self.assertEqual(r['createdLayerCount'],2)
            r=layer_split.split(source,p/'b.dxf',('entity_type',));self.assertEqual(r['createdLayerCount'],1)
    def test_byblock_preserved_and_empty_criteria_refused(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf';source.write_bytes(drawing([line('100',(0,0),(1,0))+[(62,0),(6,'ByBlock'),(370,-2)]]))
            layer_split.split(source,p/'a.dxf',('color','linetype','lineweight'))
            rows=dx.read(p/'a.dxf')[1];self.assertEqual(dx.one(rows[0],62),'0');self.assertEqual(dx.one(rows[0],6),'ByBlock');self.assertEqual(dx.one(rows[0],370),'-2')
            with self.assertRaises(ValueError):layer_split.split(source,p/'b.dxf',())
    def test_linetype_criteria_and_undefined_reference(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf'
            rows=[line('100',(0,0),(1,0)),line('101',(0,1),(1,1))+[(6,'DASHED')]]
            source.write_bytes(drawing(rows))
            with self.assertRaises(ValueError):layer_split.split(source,p/'bad.dxf',('linetype',))
            end=dx.encode([(0,'ENDTAB'),(0,'TABLE'),(2,'LAYER')])
            definition=dx.encode([(0,'LTYPE'),(5,'11'),(2,'DASHED'),(3,'Dashed'),(40,'1'),(73,2),(49,'.5'),(74,0),(49,'-.5'),(74,0)])
            source.write_bytes(drawing(rows).replace(end,definition+end,1))
            result=layer_split.split(source,p/'good.dxf',('linetype',));self.assertEqual(result['createdLayerCount'],2)
    def test_blocks_are_refused_and_missing_layer_count_is_supported(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf';source.write_bytes(fixture([line('100',(0,0),(1,0))]))
            self.assertEqual(layer_split.split(source,p/'valid.dxf',('color',))['createdLayerCount'],1)
            source.write_bytes(drawing([[(0,'INSERT'),(5,'100'),(330,'1F'),(8,'0'),(2,'Block')]]))
            with self.assertRaises(ValueError):layer_split.split(source,p/'blocked.dxf',('color',))
    def test_full_layer_job_has_navigation_and_separate_source_copy(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf';source.write_bytes(drawing([line('100',(0,0),(1,0))]))
            job=doctor.run_auto_job([source],split_criteria=('color','linetype'))
            f=job['files'][0];self.assertEqual(f['status'],'LAYER_SPLIT_COMPLETE');self.assertTrue(f['exactCopyVerified']);self.assertTrue(Path(f['candidate']).name.endswith('-LAYERS.dxf'))
    def test_mixed_annotation_preserved_byte_for_byte(self):
        with tempfile.TemporaryDirectory() as t:
            p=Path(t);source=p/'i.dxf'
            annotation=[(0,'MULTILEADER'),(5,'200'),(330,'1F'),(100,'AcDbEntity'),(8,'UP'),(100,'AcDbMLeader'),(10,3),(20,7),(62,5),(300,'Annotation')]
            source.write_bytes(drawing([line('100',(0,0),(1,0)),annotation]))
            job=doctor.run_auto_job([source],split_criteria=('color','linetype'))
            f=job['files'][0];self.assertEqual(f['status'],'LAYER_SPLIT_PARTIAL');self.assertEqual(f['layerSplit']['preservedEntityCount'],1)
            original,rows=dx.read(source);after,out=dx.read(f['candidate'])
            self.assertEqual(dx.rawrow(original,rows[1]),dx.rawrow(after,out[1]));self.assertTrue(f['sourceUnchanged'])

if __name__=='__main__':unittest.main()
