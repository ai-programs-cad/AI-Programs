#include <BRepAlgoAPI_Cut.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRep_Builder.hxx>
#include <Interface_Static.hxx>
#include <Quantity_Color.hxx>
#include <STEPCAFControl_Writer.hxx>
#include <TDocStd_Document.hxx>
#include <TopoDS_Compound.hxx>
#include <XCAFApp_Application.hxx>
#include <XCAFDoc_ColorTool.hxx>
#include <XCAFDoc_DocumentTool.hxx>
#include <XCAFDoc_ShapeTool.hxx>
#include <filesystem>
int main(int argc, char **argv) {
    if (argc != 2)
        return 2;
    auto root = std::filesystem::absolute(argv[1]);
    Handle(TDocStd_Document) doc;
    auto app = XCAFApp_Application::GetApplication();
    app->NewDocument("BinXCAF", doc);
    auto shapes = XCAFDoc_DocumentTool::ShapeTool(doc->Main());
    auto colors = XCAFDoc_DocumentTool::ColorTool(doc->Main());
    auto box = BRepPrimAPI_MakeBox(20, 30, 10).Shape();
    auto cylinder =
        BRepPrimAPI_MakeCylinder(gp_Ax2(gp_Pnt(10, 15, -1), gp_Dir(0, 0, 1)), 3, 12).Shape();
    auto part = BRepAlgoAPI_Cut(box, cylinder).Shape();
    auto def = shapes->AddShape(part, false);
    colors->SetColor(def, Quantity_Color(.2, .5, .9, Quantity_TOC_RGB), XCAFDoc_ColorGen);
    TopoDS_Compound compound;
    BRep_Builder builder;
    builder.MakeCompound(compound);
    auto assembly = shapes->AddShape(compound, true);
    gp_Trsf translate;
    translate.SetTranslation(gp_Vec(40, 0, 0));
    shapes->AddComponent(assembly, def, {});
    auto second = shapes->AddComponent(assembly, def, TopLoc_Location(translate));
    colors->SetColor(second, Quantity_Color(.9, .2, .15, Quantity_TOC_RGB), XCAFDoc_ColorGen);
    shapes->UpdateAssemblies();
    STEPCAFControl_Writer writer;
    writer.SetColorMode(true);
    if (!writer.Transfer(doc, STEPControl_AsIs) ||
        writer.Write((root / "assembly.step").string().c_str()) != IFSelect_RetDone)
        return 1;
    std::filesystem::copy_file(root / "assembly.step", root / "assembly.stp",
                               std::filesystem::copy_options::overwrite_existing);
    for (auto schema : {"AP203", "AP242DIS"}) {
        if (!Interface_Static::SetCVal("write.step.schema", schema))
            return 3;
        STEPCAFControl_Writer other;
        other.SetColorMode(true);
        auto filename = root / (std::string(schema) + ".step");
        if (!other.Transfer(doc, STEPControl_AsIs) ||
            other.Write(filename.u8string().c_str()) != IFSelect_RetDone)
            return 4;
    }
    app->Close(doc);
    return 0;
}
