#include "cad/core.hpp"
#include <cstring>
#include <fstream>
#include <iostream>
static int checks = 0;
void check(bool v, const char *message) {
    ++checks;
    if (!v)
        throw std::runtime_error(message);
}
int main(int argc, char **argv) {
    try {
        auto root = argc > 1 ? std::filesystem::path(argv[1]) : std::filesystem::path("fixtures");
        auto tmp = std::filesystem::temp_directory_path() / "cadpreview-core-test";
        std::filesystem::create_directories(tmp);
        auto write = [&](const char *n, std::string text) {
            auto p = tmp / n;
            std::ofstream f(p, std::ios::binary);
            f << text;
            return p;
        };
        auto expectError = [&](const std::filesystem::path &p) {
            bool bad = false;
            try {
                cad::load(p);
            } catch (...) {
                bad = true;
            }
            check(bad, "Corruption accepted");
        };
        auto stl = cad::load(root / "tetrahedron-ascii.stl");
        check(stl.triangles.size() == 4, "ASCII triangle count");
        check(stl.paths.size() == 6, "Tetrahedron has six feature edges");
        check(stl.bounds.size().x == 10, "ASCII bounds");
        auto orig = cad::readFile(root / "tetrahedron-ascii.stl", {});
        cad::writeScene(tmp / "test.scene", stl);
        auto copy = cad::readScene(tmp / "test.scene");
        check(copy.triangles.size() == 4, "IPC triangle roundtrip");
        check(orig == cad::readFile(root / "tetrahedron-ascii.stl", {}), "Source was changed");
        auto binary = cad::load(root / "tetrahedron-binary.stl");
        check(binary.paths.size() == 6, "Binary STL feature edges match ASCII");
        auto square = cad::load(write("square.stl",
                                      "solid square\nfacet normal 0 0 1\nouter loop\nvertex 0 0 "
                                      "0\nvertex 1 0 0\nvertex 1 1 0\nendloop\nendfacet\n"
                                      "facet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 1 "
                                      "0\nvertex 0 1 0\nendloop\nendfacet\nendsolid square\n"));
        check(square.paths.size() == 4, "Flat STL diagonal is not a feature edge");
        check(square.triangles.size() == 2, "Feature extraction preserves all source triangles");
        auto crease = cad::load(write("crease.stl",
                                      "solid crease\nfacet normal 0 0 1\nouter loop\nvertex 0 0 "
                                      "0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\n"
                                      "facet normal 0 1 0\nouter loop\nvertex 1 0 0\nvertex 0 0 "
                                      "0\nvertex 0 0 1\nendloop\nendfacet\nendsolid crease\n"));
        check(crease.paths.size() == 5, "Right angle STL crease is highlighted");
        check(binary.triangles.size() == 4, "Binary triangle count");
        check(binary.bounds.size().z == 10, "Binary bounds");
        auto dxf = cad::load(root / "geometry.dxf");
        check(dxf.flat, "DXF plane");
        check(dxf.paths.size() >= 7, "DXF entities");
        bool tiny = false, bulge = false, block = false;
        for (auto &p : dxf.paths) {
            if (p.points.size() == 2 &&
                std::abs(cad::length(p.points[1] - p.points[0]) - .01) < 1e-8)
                tiny = true;
            if (p.points.size() > 30)
                bulge = true;
            for (auto v : p.points)
                if (v.x >= 100)
                    block = true;
        }
        check(tiny, "Tiny DXF geometry lost");
        check(bulge, "Bulge tessellation absent");
        check(block, "Block placement absent");
        check(dxf.layers.size() >= 2, "DXF layers absent");
        check(!dxf.texts.empty(), "DXF text absent");
        cad::writeScene(tmp / "dxf.scene", dxf);
        auto dr = cad::readScene(tmp / "dxf.scene");
        check(dr.paths.size() == dxf.paths.size(), "DXF IPC roundtrip");
        expectError(root / "corrupt.stl");
        expectError(write("truncated.dxf", "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n1\n"));
        expectError(write("bad.stl", "solid x\nfacet normal 0 0 1\nouter loop\nvertex nan 0 0\n"));
        expectError(write("unknown.exe", "anything"));
        auto truncated = orig.substr(0, orig.size() / 2);
        expectError(write("short.stl", truncated));
        cad::Limits limits;
        limits.primitives = 1;
        bool limited = false;
        try {
            cad::load(root / "tetrahedron-ascii.stl", limits);
        } catch (...) {
            limited = true;
        }
        check(limited, "Triangle memory limit ignored");
        std::ofstream(tmp / "broken.scene", std::ios::binary) << "CDP3";
        bool bad = false;
        try {
            cad::readScene(tmp / "broken.scene");
        } catch (...) {
            bad = true;
        }
        check(bad, "Truncated IPC accepted");
        if (std::filesystem::exists(root / "assembly.step")) {
            auto step = cad::load(root / "assembly.step");
            auto size = step.bounds.size();
            check(std::abs(size.x - 60) < 1e-5 && std::abs(size.y - 30) < 1e-5 &&
                      std::abs(size.z - 10) < 1e-5,
                  "STEP assembly placements or units");
            check(step.triangles.size() > 40, "STEP triangulation");
            bool red = false, blue = false;
            for (auto &t : step.triangles) {
                red |= t.color.r > t.color.b;
                blue |= t.color.b > t.color.r;
            }
            check(red && blue, "STEP instance colors");
            check(!step.paths.empty(), "STEP BRep edges");
            auto cp = tmp / "assembly.stp";
            std::filesystem::copy_file(root / "assembly.step", cp,
                                       std::filesystem::copy_options::overwrite_existing);
            check(cad::load(cp).triangles.size() == step.triangles.size(), "STP extension");
        }
        auto largeMesh = cad::load(root / "large-mesh.stl");
        check(largeMesh.triangles.size() == 100000, "Large STL count");
        auto twoBodies = cad::load(root / "two-bodies.stl");
        check(twoBodies.triangles.size() == 8 && twoBodies.bounds.size().x == 40,
              "Disconnected STL components");
        auto largeDrawing = cad::load(root / "large-drawing.dxf");
        check(largeDrawing.paths.size() == 10000, "Large DXF entity count");
        check(largeDrawing.bounds.size().x == 9999, "Large DXF bounds");
        expectError(write("recursion.dxf",
                          "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nR\n0\nINSERT\n2\nR\n0\nENDBLK\n0\nEN"
                          "DSEC\n0\nSECTION\n2\nENTITIES\n0\nINSERT\n2\nR\n0\nENDSEC\n0\nEOF\n"));
        for (auto schema : {"AP203.step", "AP242DIS.step"}) {
            if (std::filesystem::exists(root / schema)) {
                auto imported = cad::load(root / schema);
                check(std::abs(imported.bounds.size().x - 60) < 1e-5,
                      "STEP schema assembly placement");
                check(imported.triangles.size() > 40, "STEP schema triangulation");
            }
        }
        std::cout << checks << " checks passed\n";
        std::error_code ec;
        std::filesystem::remove_all(tmp, ec);
        return 0;
    } catch (const std::exception &e) {
        std::cerr << "FAIL after " << checks << " checks: " << e.what() << "\n";
        return 1;
    }
}
