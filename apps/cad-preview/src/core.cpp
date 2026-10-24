#include "cad/core.hpp"
#include <algorithm>
#include <cctype>
#include <fstream>
namespace cad {
void Bounds::add(Vec p) {
    if (!std::isfinite(p.x) || !std::isfinite(p.y) || !std::isfinite(p.z) || std::abs(p.x) > 1e15 ||
        std::abs(p.y) > 1e15 || std::abs(p.z) > 1e15)
        throw Error("Invalid or excessive coordinate");
    if (empty) {
        min = max = p;
        empty = false;
    } else {
        min = {std::min(min.x, p.x), std::min(min.y, p.y), std::min(min.z, p.z)};
        max = {std::max(max.x, p.x), std::max(max.y, p.y), std::max(max.z, p.z)};
    }
}
void Scene::finalize() {
    bounds = {};
    for (auto &p : paths)
        for (auto v : p.points)
            bounds.add(v);
    for (auto &t : triangles) {
        bounds.add(t.a);
        bounds.add(t.b);
        bounds.add(t.c);
    }
    for (auto &t : texts) {
        bounds.add(t.position);
        bounds.add(t.position + Vec{t.height * double(t.value.size()) * .6, t.height, 0});
    }
    if (bounds.empty)
        throw Error("No supported geometry found");
}
std::string readFile(const std::filesystem::path &p, const Limits &l) {
    std::error_code ec;
    auto n = std::filesystem::file_size(p, ec);
    if (ec || n > l.fileBytes)
        throw Error("File is inaccessible or exceeds size limit");
    std::ifstream f(p, std::ios::binary);
    std::string s(size_t(n), '\0');
    if (!f.read(s.data(), std::streamsize(n)))
        throw Error("Cannot read file");
    return s;
}
Scene load(const std::filesystem::path &p, const Limits &l) {
    std::string e = p.extension().string();
    std::transform(e.begin(), e.end(), e.begin(),
                   [](unsigned char c) { return char(std::tolower(c)); });
    Scene s;
    if (e == ".dxf")
        s = loadDxf(p, l);
    else if (e == ".stl")
        s = loadStl(p, l);
    else if (e == ".step" || e == ".stp") {
#ifdef CADPREVIEW_WITH_OCCT
        s = loadStep(p, l);
#else
        throw Error("This developer build does not include OCCT");
#endif
    } else
        throw Error("Unsupported file extension");
    s.finalize();
    return s;
}
} // namespace cad
