#include "cad/core.hpp"
#include <algorithm>
#include <cstring>
#include <locale>
#include <sstream>
#include <tuple>
namespace cad {
static uint32_t u32(const char *p) {
    return uint32_t(uint8_t(p[0])) | (uint32_t(uint8_t(p[1])) << 8) |
           (uint32_t(uint8_t(p[2])) << 16) | (uint32_t(uint8_t(p[3])) << 24);
}
static double f32(const char *p) {
    uint32_t u = u32(p);
    float f;
    std::memcpy(&f, &u, 4);
    return f;
}
Scene loadStl(const std::filesystem::path &p, const Limits &l) {
    auto data = readFile(p, l);
    Scene s;
    s.format = "STL";
    s.layers.push_back({"Model", {}});
    auto add = [&](Vec a, Vec b, Vec c) {
        Bounds check;
        check.add(a);
        check.add(b);
        check.add(c);
        if (s.triangles.size() >= l.primitives)
            throw Error("Triangle limit exceeded");
        s.triangles.push_back({a, b, c, {165, 190, 220, 255}});
    };
    if (data.size() >= 84 && 84ull + 50ull * u32(data.data() + 80) == data.size()) {
        auto n = u32(data.data() + 80);
        if (n > l.primitives)
            throw Error("Triangle limit exceeded");
        s.triangles.reserve(n);
        for (uint32_t i = 0; i < n; ++i) {
            const char *q = data.data() + 84 + size_t(i) * 50 + 12;
            auto v = [&](int j) { return Vec{f32(q + j), f32(q + j + 4), f32(q + j + 8)}; };
            add(v(0), v(12), v(24));
        }
    } else {
        std::istringstream in(data);
        in.imbue(std::locale::classic());
        std::string w, line;
        if (!(in >> w) || w != "solid")
            throw Error("Invalid STL header or truncated binary STL");
        std::getline(in, line);
        bool ended = false;
        while (in >> w) {
            if (w == "endsolid") {
                ended = true;
                break;
            }
            if (w != "facet" || !(in >> w) || w != "normal")
                throw Error("Malformed ASCII STL facet");
            double nx, ny, nz;
            if (!(in >> nx >> ny >> nz >> w) || w != "outer" || !(in >> w) || w != "loop")
                throw Error("Malformed STL loop");
            Vec v[3];
            for (auto &a : v)
                if (!(in >> w) || w != "vertex" || !(in >> a.x >> a.y >> a.z))
                    throw Error("Malformed STL vertex");
            if (!(in >> w) || w != "endloop" || !(in >> w) || w != "endfacet")
                throw Error("Truncated STL facet");
            add(v[0], v[1], v[2]);
        }
        if (!ended)
            throw Error("Missing endsolid");
    }
    // Exact endpoint matching preserves all source triangles and never welds small geometry.
    // Keep boundary/non-manifold edges and creases of at least 30 degrees, not tessellation seams.
    struct Edge {
        Vec a, b;
        uint32_t triangle;
    };
    auto lessPoint = [](Vec a, Vec b) { return std::tie(a.x, a.y, a.z) < std::tie(b.x, b.y, b.z); };
    std::vector<Edge> edges;
    edges.reserve(s.triangles.size() * 3);
    for (uint32_t i = 0; i < s.triangles.size(); ++i) {
        const auto &t = s.triangles[i];
        for (auto pair :
             {std::make_pair(t.a, t.b), std::make_pair(t.b, t.c), std::make_pair(t.c, t.a)}) {
            if (lessPoint(pair.second, pair.first))
                std::swap(pair.first, pair.second);
            edges.push_back({pair.first, pair.second, i});
        }
    }
    auto lessEdge = [&](const Edge &a, const Edge &b) {
        if (lessPoint(a.a, b.a))
            return true;
        if (lessPoint(b.a, a.a))
            return false;
        return lessPoint(a.b, b.b);
    };
    std::sort(edges.begin(), edges.end(), lessEdge);
    auto normal = [&](uint32_t index) {
        const auto &t = s.triangles[index];
        return cross(t.b - t.a, t.c - t.a);
    };
    for (size_t first = 0; first < edges.size();) {
        size_t end = first + 1;
        while (end < edges.size() && !lessEdge(edges[first], edges[end]))
            ++end;
        bool feature = end - first != 2;
        if (!feature) {
            auto a = normal(edges[first].triangle), b = normal(edges[first + 1].triangle);
            double magnitude = length(a) * length(b);
            feature = magnitude > 0 && std::abs(dot(a, b)) <= .8660254037844386 * magnitude;
        }
        if (feature && length(edges[first].b - edges[first].a) > 0) {
            if (s.paths.size() >= l.primitives || 2 * (s.paths.size() + 1) > l.points)
                throw Error("STL feature edge limit exceeded");
            Path path;
            path.points = {edges[first].a, edges[first].b};
            s.paths.push_back(std::move(path));
        }
        first = end;
    }
    return s;
}
} // namespace cad
