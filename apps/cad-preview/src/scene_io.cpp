#include "cad/core.hpp"
#include <fstream>
#include <type_traits>
namespace cad {
namespace {
template <class T> void put(std::ostream &o, const T &v) {
    static_assert(std::is_trivially_copyable_v<T>);
    o.write(reinterpret_cast<const char *>(&v), sizeof(v));
}
template <class T> T get(std::istream &i) {
    T v{};
    if (!i.read(reinterpret_cast<char *>(&v), sizeof(v)))
        throw Error("Truncated scene");
    return v;
}
void str(std::ostream &o, const std::string &s) {
    put(o, uint32_t(s.size()));
    o.write(s.data(), s.size());
}
std::string str(std::istream &i) {
    auto n = get<uint32_t>(i);
    if (n > 1024 * 1024)
        throw Error("Scene string limit");
    std::string s(n, '\0');
    if (!i.read(s.data(), n))
        throw Error("Truncated scene string");
    return s;
}
uint32_t count(std::istream &i, uint64_t max) {
    auto n = get<uint32_t>(i);
    if (n > max)
        throw Error("Scene allocation limit");
    return n;
}
} // namespace
void writeScene(const std::filesystem::path &p, const Scene &s) {
    std::ofstream o(p, std::ios::binary);
    put(o, uint32_t(0x33504443));
    put(o, uint8_t(s.flat));
    str(o, s.format);
    str(o, s.units);
    put(o, uint32_t(s.layers.size()));
    for (auto &l : s.layers) {
        str(o, l.name);
        put(o, l.color);
        put(o, uint8_t(l.visible));
    }
    put(o, uint32_t(s.paths.size()));
    for (auto &a : s.paths) {
        put(o, a.color);
        put(o, a.layer);
        put(o, uint8_t(a.closed));
        put(o, uint8_t(a.filled));
        put(o, a.lineType);
        put(o, uint32_t(a.points.size()));
        for (auto v : a.points)
            put(o, v);
    }
    put(o, uint32_t(s.triangles.size()));
    for (auto &t : s.triangles)
        put(o, t);
    put(o, uint32_t(s.texts.size()));
    for (auto &t : s.texts) {
        put(o, t.position);
        str(o, t.value);
        put(o, t.height);
        put(o, t.angle);
        put(o, t.layer);
        put(o, t.color);
    }
    put(o, uint32_t(s.warnings.size()));
    for (auto &w : s.warnings)
        str(o, w);
    if (!o)
        throw Error("Cannot write worker scene");
}
Scene readScene(const std::filesystem::path &p, const Limits &lim) {
    if (std::filesystem::file_size(p) > lim.fileBytes * 4)
        throw Error("Worker output size limit");
    std::ifstream i(p, std::ios::binary);
    if (get<uint32_t>(i) != 0x33504443)
        throw Error("Worker protocol mismatch");
    Scene s;
    s.flat = get<uint8_t>(i) != 0;
    s.format = str(i);
    s.units = str(i);
    auto n = count(i, 65536);
    for (uint32_t j = 0; j < n; ++j) {
        Layer l;
        l.name = str(i);
        l.color = get<Color>(i);
        l.visible = get<uint8_t>(i) != 0;
        s.layers.push_back(l);
    }
    n = count(i, lim.primitives);
    uint64_t points = 0;
    for (uint32_t j = 0; j < n; ++j) {
        Path a;
        a.color = get<Color>(i);
        a.layer = get<uint32_t>(i);
        a.closed = get<uint8_t>(i) != 0;
        a.filled = get<uint8_t>(i) != 0;
        a.lineType = get<int>(i);
        auto m = count(i, lim.points);
        points += m;
        if (points > lim.points)
            throw Error("Scene point limit");
        for (uint32_t k = 0; k < m; ++k)
            a.points.push_back(get<Vec>(i));
        if (a.layer >= s.layers.size())
            throw Error("Invalid scene layer");
        s.paths.push_back(std::move(a));
    }
    n = count(i, lim.primitives);
    for (uint32_t j = 0; j < n; ++j)
        s.triangles.push_back(get<Triangle>(i));
    n = count(i, lim.primitives);
    for (uint32_t j = 0; j < n; ++j) {
        Text t;
        t.position = get<Vec>(i);
        t.value = str(i);
        t.height = get<double>(i);
        t.angle = get<double>(i);
        t.layer = get<uint32_t>(i);
        t.color = get<Color>(i);
        if (t.layer >= s.layers.size() || !std::isfinite(t.height) || t.height <= 0)
            throw Error("Invalid scene text");
        s.texts.push_back(t);
    }
    n = count(i, 4096);
    for (uint32_t j = 0; j < n; ++j)
        s.warnings.push_back(str(i));
    s.finalize();
    return s;
}
} // namespace cad
