#include "cad/core.hpp"
#include <BRepAdaptor_Curve.hxx>
#include <BRepBndLib.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRep_Tool.hxx>
#include <Bnd_Box.hxx>
#include <GCPnts_QuasiUniformDeflection.hxx>
#include <Poly_Triangulation.hxx>
#include <Quantity_Color.hxx>
#include <STEPCAFControl_Reader.hxx>
#include <Standard_Failure.hxx>
#include <TDF_LabelSequence.hxx>
#include <TDocStd_Document.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <XCAFApp_Application.hxx>
#include <XCAFDoc_ColorTool.hxx>
#include <XCAFDoc_DocumentTool.hxx>
#include <XCAFDoc_ShapeTool.hxx>
#include <functional>
namespace cad {
Scene loadStep(const std::filesystem::path &path, const Limits &lim) {
    if (std::filesystem::file_size(path) > lim.fileBytes)
        throw Error("STEP exceeds file limit");
    try {
        Handle(TDocStd_Document) doc;
        auto app = XCAFApp_Application::GetApplication();
        app->NewDocument("BinXCAF", doc);
        struct Close {
            Handle(TDocStd_Document) doc;
            ~Close() { XCAFApp_Application::GetApplication()->Close(doc); }
        } close{doc};
        STEPCAFControl_Reader reader;
        reader.SetColorMode(true);
        reader.SetNameMode(true);
        reader.SetLayerMode(true);
        // OCCT 7.x uses narrow paths. Worker stages the source to an ASCII basename.
        if (reader.ReadFile(path.u8string().c_str()) != IFSelect_RetDone || !reader.Transfer(doc))
            throw Error("OCCT could not import STEP");
        auto shapes = XCAFDoc_DocumentTool::ShapeTool(doc->Main());
        auto colors = XCAFDoc_DocumentTool::ColorTool(doc->Main());
        Scene s;
        s.format = "STEP (OCCT XCAF)";
        s.units = "mm (OCCT import units)";
        s.layers.push_back({"Model", {}});
        TDF_LabelSequence roots;
        shapes->GetFreeShapes(roots);
        uint64_t components = 0;
        auto labelColor = [&](const TDF_Label &l, Color inherited) {
            Quantity_Color c;
            for (auto type : {XCAFDoc_ColorSurf, XCAFDoc_ColorGen})
                if (colors->GetColor(l, type, c))
                    return Color{uint8_t(c.Red() * 255), uint8_t(c.Green() * 255),
                                 uint8_t(c.Blue() * 255), 255};
            return inherited;
        };
        std::function<void(TDF_Label, TopLoc_Location, Color, unsigned)> visit;
        visit = [&](TDF_Label label, TopLoc_Location parent, Color inherited, unsigned depth) {
            if (depth > lim.blockDepth || ++components > lim.primitives)
                throw Error("STEP assembly limit exceeded");
            Color color = labelColor(label, inherited);
            TopLoc_Location loc = parent * shapes->GetLocation(label);
            TDF_Label referred;
            if (shapes->GetReferredShape(label, referred)) {
                color = labelColor(label, labelColor(referred, inherited));
                label = referred;
            }
            TDF_LabelSequence children;
            if (shapes->GetComponents(label, children, false)) {
                for (int j = 1; j <= children.Length(); ++j)
                    visit(children.Value(j), loc, color, depth + 1);
                return;
            }
            TopoDS_Shape shape = shapes->GetShape(label);
            if (shape.IsNull())
                return;
            // Strip definition location: placement is accumulated exactly once per instance.
            shape.Location(TopLoc_Location());
            Bnd_Box box;
            BRepBndLib::Add(shape, box);
            double x0, y0, z0, x1, y1, z1;
            box.Get(x0, y0, z0, x1, y1, z1);
            double diagonal = length(Vec{x1 - x0, y1 - y0, z1 - z0});
            double deflection = std::max(1e-7, std::min(lim.deflection, diagonal * .002));
            BRepMesh_IncrementalMesh mesh(shape, deflection, false, .35, false);
            if (!mesh.IsDone())
                throw Error("OCCT triangulation failed");
            for (TopExp_Explorer ex(shape, TopAbs_FACE); ex.More(); ex.Next()) {
                TopoDS_Face face = TopoDS::Face(ex.Current());
                Color fc = color;
                Quantity_Color qc;
                if (colors->GetColor(face, XCAFDoc_ColorSurf, qc) ||
                    colors->GetColor(face, XCAFDoc_ColorGen, qc))
                    fc = {uint8_t(qc.Red() * 255), uint8_t(qc.Green() * 255),
                          uint8_t(qc.Blue() * 255), 255};
                TopLoc_Location fl;
                auto tri = BRep_Tool::Triangulation(face, fl);
                if (tri.IsNull())
                    continue;
                auto tr = (loc * fl).Transformation();
                if (s.triangles.size() + tri->NbTriangles() > lim.primitives)
                    throw Error("STEP triangle limit exceeded");
                for (int j = 1; j <= tri->NbTriangles(); ++j) {
                    int a, b, c;
                    tri->Triangle(j).Get(a, b, c);
                    if (face.Orientation() == TopAbs_REVERSED)
                        std::swap(b, c);
                    auto v = [&](int n) {
                        auto p = tri->Node(n).Transformed(tr);
                        return Vec{p.X(), p.Y(), p.Z()};
                    };
                    s.triangles.push_back({v(a), v(b), v(c), fc});
                }
            }
            uint64_t pointCount = 0;
            for (TopExp_Explorer ex(shape, TopAbs_EDGE); ex.More(); ex.Next()) {
                auto edge = TopoDS::Edge(ex.Current());
                if (BRep_Tool::Degenerated(edge))
                    continue;
                BRepAdaptor_Curve curve(edge);
                GCPnts_QuasiUniformDeflection sampled(curve, deflection);
                if (!sampled.IsDone())
                    continue;
                Path path;
                path.color = color;
                path.layer = 0;
                pointCount += sampled.NbPoints();
                if (pointCount > lim.points || s.paths.size() >= lim.primitives)
                    throw Error("STEP edge limit exceeded");
                for (int j = 1; j <= sampled.NbPoints(); ++j) {
                    auto p = sampled.Value(j).Transformed(loc.Transformation());
                    path.points.push_back({p.X(), p.Y(), p.Z()});
                }
                s.paths.push_back(std::move(path));
            }
        };
        for (int j = 1; j <= roots.Length(); ++j)
            visit(roots.Value(j), {}, Color{165, 190, 220, 255}, 0);
        return s;
    } catch (const Standard_Failure &e) {
        throw Error(std::string("OCCT: ") +
                    (e.GetMessageString() ? e.GetMessageString() : "import failure"));
    }
}
} // namespace cad
