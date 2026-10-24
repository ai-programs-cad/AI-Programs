#include "cad/core.hpp"
#include <algorithm>
#include <functional>
#include <limits>
#include <locale>
#include <map>
#include <set>
#include <sstream>
namespace cad {
namespace {
constexpr double pi = 3.14159265358979323846;
struct Pair {
    int code;
    std::string value;
};
struct Entity {
    std::string type;
    std::vector<Pair> data;
    std::string str(int c, std::string d = "") const {
        for (auto &p : data)
            if (p.code == c)
                return p.value;
        return d;
    }
    double num(int c, double d = 0) const {
        auto s = str(c);
        if (s.empty())
            return d;
        size_t used;
        double v;
        try {
            v = std::stod(s, &used);
        } catch (...) {
            throw Error("Invalid DXF number");
        }
        if (used != s.size() || !std::isfinite(v))
            throw Error("Invalid DXF number");
        return v;
    }
    Vec v(int c = 10) const { return {num(c), num(c + 10), num(c + 20)}; }
    int integer(int c, int d = 0) const {
        double value = num(c, d);
        if (value < std::numeric_limits<int>::min() || value > std::numeric_limits<int>::max() ||
            std::trunc(value) != value)
            throw Error("Invalid DXF integer");
        return int(value);
    }
};
struct Matrix {
    double a = 1, b = 0, c = 0, d = 1, tx = 0, ty = 0;
    Vec apply(Vec v) const { return {a * v.x + c * v.y + tx, b * v.x + d * v.y + ty, v.z}; }
    Matrix then(const Matrix &o) const {
        return {a * o.a + c * o.b, b * o.a + d * o.b,        a * o.c + c * o.d,
                b * o.c + d * o.d, a * o.tx + c * o.ty + tx, b * o.tx + d * o.ty + ty};
    }
};
Color aci(int n) {
    static Color base[] = {{255, 255, 255, 255}, {255, 0, 0, 255},     {255, 255, 0, 255},
                           {0, 255, 0, 255},     {0, 255, 255, 255},   {0, 0, 255, 255},
                           {255, 0, 255, 255},   {255, 255, 255, 255}, {128, 128, 128, 255},
                           {192, 192, 192, 255}};
    if (n < -255 || n > 255)
        n = 7;
    n = std::abs(n);
    if (n < 10)
        return base[n];
    if (n >= 250) {
        static const uint8_t gray[] = {51, 80, 105, 130, 190, 255};
        uint8_t q = gray[n - 250];
        return {q, q, q, 255};
    }
    static const double values[] = {255, 165, 127, 76, 38};
    double h = ((n - 10) / 10) * .25, value = values[((n - 10) % 10) / 2];
    double c = value * (n % 2 ? .5 : 1.), x = c * (1 - std::abs(std::fmod(h, 2.) - 1)),
           m = value - c;
    Vec rgb;
    switch (int(h)) {
    case 0:
        rgb = {c, x, 0};
        break;
    case 1:
        rgb = {x, c, 0};
        break;
    case 2:
        rgb = {0, c, x};
        break;
    case 3:
        rgb = {0, x, c};
        break;
    case 4:
        rgb = {x, 0, c};
        break;
    default:
        rgb = {c, 0, x};
        break;
    }
    return {uint8_t(rgb.x + m), uint8_t(rgb.y + m), uint8_t(rgb.z + m), 255};
}
std::string trim(std::string s) {
    auto a = s.find_first_not_of(" \t\r");
    if (a == std::string::npos)
        return {};
    return s.substr(a, s.find_last_not_of(" \t\r") - a + 1);
}
void arcPoints(std::vector<Vec> &out, Vec c, double rx, double ry, double start, double sweep,
               double rot = 0) {
    if (!std::isfinite(start) || !std::isfinite(sweep) || !std::isfinite(rx) || !std::isfinite(ry))
        throw Error("Invalid DXF curve");
    start = std::fmod(start, 2 * pi);
    sweep = std::clamp(sweep, -2 * pi, 2 * pi);
    unsigned n = std::max(2u, unsigned(std::ceil(std::abs(sweep) * 128 / pi)));
    for (unsigned i = 0; i <= n; ++i) {
        double t = start + sweep * i / n, x = rx * std::cos(t), y = ry * std::sin(t);
        out.push_back({c.x + x * std::cos(rot) - y * std::sin(rot),
                       c.y + x * std::sin(rot) + y * std::cos(rot), c.z});
    }
}
void segment(std::vector<Vec> &out, Vec a, Vec b, double bulge) {
    out.push_back(a);
    if (bulge == 0)
        return;
    double chord = length(b - a);
    if (chord == 0)
        return;
    double theta = 4 * std::atan(bulge), off = chord * (1 - bulge * bulge) / (4 * bulge);
    Vec mid = (a + b) * .5, delta = b - a,
        c = {mid.x - delta.y / chord * off, mid.y + delta.x / chord * off, a.z};
    std::vector<Vec> arc;
    arcPoints(arc, c, length(a - c), length(a - c), std::atan2(a.y - c.y, a.x - c.x), theta);
    out.insert(out.end(), arc.begin() + 1, arc.end() - 1);
}
} // namespace
Scene loadDxf(const std::filesystem::path &file, const Limits &lim) {
    std::string bytes = readFile(file, lim);
    if (bytes.find("AutoCAD Binary DXF") == 0)
        throw Error("Binary DXF is not supported in this build; use ASCII DXF");
    std::istringstream in(bytes);
    in.imbue(std::locale::classic());
    std::vector<Entity> all;
    std::string code, value;
    bool eof = false;
    while (std::getline(in, code)) {
        if (!std::getline(in, value))
            throw Error("Truncated DXF group pair");
        code = trim(code);
        int c;
        size_t used;
        try {
            c = std::stoi(code, &used);
        } catch (...) {
            throw Error("Invalid DXF group code");
        }
        if (used != code.size())
            throw Error("Invalid DXF group code");
        value = trim(value);
        if (c == 0) {
            if (all.size() >= lim.primitives)
                throw Error("DXF entity limit exceeded");
            all.push_back({value, {}});
            if (value == "EOF")
                eof = true;
        } else if (!all.empty())
            all.back().data.push_back({c, value});
    }
    if (!eof)
        throw Error("Missing DXF EOF marker");
    Scene s;
    s.flat = true;
    s.format = "DXF";
    std::map<std::string, uint32_t> layerIds;
    auto layer = [&](std::string name) -> uint32_t {
        auto it = layerIds.find(name);
        if (it != layerIds.end())
            return it->second;
        uint32_t id = uint32_t(s.layers.size());
        s.layers.push_back({name, aci(7), true});
        layerIds[name] = id;
        return id;
    };
    layer("0");
    std::map<std::string, std::vector<Entity>> blocks;
    std::map<std::string, Vec> origins;
    std::vector<Entity> model;
    std::string section, block;
    for (auto &e : all) {
        if (e.type == "SECTION") {
            section = e.str(2);
            continue;
        }
        if (e.type == "ENDSEC") {
            section.clear();
            continue;
        }
        if (section == "TABLES" && e.type == "LAYER") {
            auto id = layer(e.str(2, "0"));
            s.layers[id].color = aci(e.integer(62, 7));
            s.layers[id].visible = e.num(62, 7) >= 0 && (e.integer(70) & 1) == 0;
        } else if (section == "BLOCKS") {
            if (e.type == "BLOCK") {
                block = e.str(2);
                origins[block] = e.v();
            } else if (e.type == "ENDBLK")
                block.clear();
            else if (!block.empty())
                blocks[block].push_back(e);
        } else if (section == "ENTITIES" && e.num(67) == 0)
            model.push_back(e);
    }
    uint64_t pointCount = 0;
    std::set<std::string> warnings;
    std::function<void(const std::vector<Entity> &, Matrix, unsigned, uint32_t, Color)> emit;
    emit = [&](const std::vector<Entity> &entities, Matrix mat, unsigned depth, uint32_t inherited,
               Color blockColor) {
        if (depth > lim.blockDepth)
            throw Error("DXF block recursion limit exceeded");
        for (size_t i = 0; i < entities.size(); ++i) {
            const auto &e = entities[i];
            std::string ln = e.str(8, "0");
            auto lid = ln == "0" && depth ? inherited : layer(ln);
            int ci = e.integer(62, 256);
            Color color = ci == 256 ? s.layers[lid].color : ci == 0 ? blockColor : aci(ci);
            if (!e.str(420).empty()) {
                uint32_t rgb = uint32_t(e.integer(420));
                color = {uint8_t(rgb >> 16), uint8_t(rgb >> 8), uint8_t(rgb), 255};
            }
            if (e.num(230, 1) != 1 || e.num(210) != 0 || e.num(220) != 0) {
                warnings.insert("Non-default extrusion/OCS is not supported");
                continue;
            }
            Path p;
            p.layer = lid;
            p.color = color;
            p.lineType = e.str(6) == "DASHED" ? 1 : e.str(6) == "CENTER" ? 2 : 0;
            if (e.type == "LINE")
                p.points = {e.v(), e.v(11)};
            else if (e.type == "POINT") {
                auto v = e.v();
                p.points = {v, v};
            } else if (e.type == "CIRCLE" || e.type == "ARC") {
                double a = std::fmod(e.num(50), 360.) * pi / 180,
                       b = e.type == "CIRCLE" ? a + 2 * pi : std::fmod(e.num(51), 360.) * pi / 180;
                while (b <= a)
                    b += 2 * pi;
                double r = e.num(40);
                if (r < 0)
                    throw Error("Negative DXF radius");
                arcPoints(p.points, e.v(), r, r, a, b - a);
                p.closed = e.type == "CIRCLE";
            } else if (e.type == "ELLIPSE") {
                Vec axis = e.v(11);
                double a = std::fmod(e.num(41), 2 * pi), b = std::fmod(e.num(42, 2 * pi), 2 * pi);
                while (b <= a)
                    b += 2 * pi;
                arcPoints(p.points, e.v(), length(axis), length(axis) * e.num(40, 1), a, b - a,
                          std::atan2(axis.y, axis.x));
                p.closed = b - a >= 2 * pi;
            } else if (e.type == "LWPOLYLINE" || e.type == "POLYLINE") {
                std::vector<Vec> v;
                std::vector<double> bulge;
                if (e.type == "LWPOLYLINE") {
                    for (auto &q : e.data) {
                        if (q.code == 10) {
                            v.push_back({std::stod(q.value), 0, e.num(38)});
                            bulge.push_back(0);
                        } else if (q.code == 20 && !v.empty())
                            v.back().y = std::stod(q.value);
                        else if (q.code == 42 && !v.empty())
                            bulge.back() = std::stod(q.value);
                    }
                } else {
                    if (e.integer(70) & 64) {
                        warnings.insert("Polyface DXF meshes unsupported");
                        continue;
                    }
                    while (i + 1 < entities.size() && entities[i + 1].type == "VERTEX") {
                        const auto &vtx = entities[++i];
                        v.push_back(vtx.v());
                        bulge.push_back(vtx.num(42));
                    }
                    if (i + 1 < entities.size() && entities[i + 1].type == "SEQEND")
                        ++i;
                }
                p.closed = (e.integer(70) & 1) != 0;
                for (size_t j = 0; j < v.size(); ++j) {
                    if (j + 1 < v.size() || p.closed)
                        segment(p.points, v[j], v[(j + 1) % v.size()], bulge[j]);
                    else
                        p.points.push_back(v[j]);
                }
            } else if (e.type == "SOLID" || e.type == "TRACE" || e.type == "3DFACE") {
                p.points = {e.v(), e.v(11), e.v(13), e.v(12)};
                p.closed = p.filled = true;
            } else if (e.type == "TEXT" || e.type == "MTEXT") {
                std::string text;
                for (auto &q : e.data)
                    if (q.code == 3 || q.code == 1)
                        text += q.value;
                s.texts.push_back({mat.apply(e.v()), text, e.num(40, 1) * std::hypot(mat.a, mat.b),
                                   e.num(50) + std::atan2(mat.b, mat.a) * 180 / pi, lid, color});
                continue;
            } else if (e.type == "INSERT") {
                std::string name = e.str(2);
                if (!blocks.count(name)) {
                    warnings.insert("Missing block: " + name);
                    continue;
                }
                double a = e.num(50) * pi / 180, sx = e.num(41, 1), sy = e.num(42, 1);
                Vec o = origins[name], v = e.v();
                int cols = std::clamp(e.integer(70, 1), 1, 1000),
                    rows = std::clamp(e.integer(71, 1), 1, 1000);
                if (uint64_t(cols) * rows > lim.primitives)
                    throw Error("Block array limit");
                for (int r = 0; r < rows; ++r)
                    for (int c = 0; c < cols; ++c) {
                        Matrix m{sx * std::cos(a),
                                 sx * std::sin(a),
                                 -sy * std::sin(a),
                                 sy * std::cos(a),
                                 v.x,
                                 v.y};
                        m.tx -= m.a * o.x + m.c * o.y;
                        m.ty -= m.b * o.x + m.d * o.y;
                        m.tx += std::cos(a) * c * e.num(44) - std::sin(a) * r * e.num(45);
                        m.ty += std::sin(a) * c * e.num(44) + std::cos(a) * r * e.num(45);
                        emit(blocks[name], mat.then(m), depth + 1, lid, color);
                    }
                continue;
            } else if (e.type == "SPLINE") {
                std::vector<Vec> ctrl;
                std::vector<double> knots, weights;
                for (auto &q : e.data) {
                    if (q.code == 10)
                        ctrl.push_back({std::stod(q.value), 0, 0});
                    else if (q.code == 20 && !ctrl.empty())
                        ctrl.back().y = std::stod(q.value);
                    else if (q.code == 30 && !ctrl.empty())
                        ctrl.back().z = std::stod(q.value);
                    else if (q.code == 40)
                        knots.push_back(std::stod(q.value));
                    else if (q.code == 41)
                        weights.push_back(std::stod(q.value));
                }
                int degree = e.integer(71, 3);
                if (degree < 1 || degree > 16 || ctrl.size() <= size_t(degree) ||
                    knots.size() != ctrl.size() + degree + 1) {
                    warnings.insert("Invalid or fit-point-only spline skipped");
                    continue;
                }
                if (weights.empty())
                    weights.assign(ctrl.size(), 1);
                if (weights.size() != ctrl.size() || !std::is_sorted(knots.begin(), knots.end()))
                    throw Error("Invalid spline knots or weights");
                unsigned n = std::min(8192u, std::max(64u, unsigned(ctrl.size() * 16)));
                double lo = knots[degree], hi = knots[ctrl.size()];
                for (unsigned k = 0; k <= n; ++k) {
                    double t = k == n ? std::nextafter(hi, lo) : lo + (hi - lo) * k / n;
                    std::vector<double> basis(ctrl.size() + degree, 0);
                    for (size_t j = 0; j < ctrl.size(); ++j)
                        basis[j] = knots[j] <= t && t < knots[j + 1] ? 1 : 0;
                    for (int d = 1; d <= degree; ++d)
                        for (size_t j = 0; j < ctrl.size(); ++j) {
                            double left = knots[j + d] - knots[j],
                                   right = knots[j + d + 1] - knots[j + 1];
                            basis[j] =
                                (left ? ((t - knots[j]) / left) * basis[j] : 0) +
                                (right ? ((knots[j + d + 1] - t) / right) * basis[j + 1] : 0);
                        }
                    Vec v;
                    double w = 0;
                    for (size_t j = 0; j < ctrl.size(); ++j) {
                        v = v + ctrl[j] * (basis[j] * weights[j]);
                        w += basis[j] * weights[j];
                    }
                    if (w != 0)
                        p.points.push_back(v * (1 / w));
                }
            } else if (e.type == "HATCH") {
                std::vector<size_t> starts;
                for (size_t k = 0; k < e.data.size(); ++k)
                    if (e.data[k].code == 92)
                        starts.push_back(k);
                starts.push_back(e.data.size());
                for (size_t loop = 0; loop + 1 < starts.size(); ++loop) {
                    Entity boundary;
                    boundary.data.assign(e.data.begin() + starts[loop] + 1,
                                         e.data.begin() + starts[loop + 1]);
                    Path hp = p;
                    hp.closed = true;
                    hp.filled = e.num(70) == 1 && starts.size() == 2;
                    int flags = std::stoi(e.data[starts[loop]].value);
                    if (flags & 2) {
                        std::vector<Vec> v;
                        std::vector<double> bulges;
                        for (auto &q : boundary.data) {
                            if (q.code == 10) {
                                v.push_back({std::stod(q.value), 0, 0});
                                bulges.push_back(0);
                            } else if (q.code == 20 && !v.empty())
                                v.back().y = std::stod(q.value);
                            else if (q.code == 42 && !v.empty())
                                bulges.back() = std::stod(q.value);
                            else if (q.code == 97)
                                break;
                        }
                        for (size_t k = 0; k < v.size(); ++k)
                            segment(hp.points, v[k], v[(k + 1) % v.size()], bulges[k]);
                    } else {
                        std::vector<size_t> edges;
                        for (size_t k = 0; k < boundary.data.size(); ++k)
                            if (boundary.data[k].code == 72)
                                edges.push_back(k);
                        edges.push_back(boundary.data.size());
                        for (size_t k = 0; k + 1 < edges.size(); ++k) {
                            int type = std::stoi(boundary.data[edges[k]].value);
                            Entity edge;
                            edge.data.assign(boundary.data.begin() + edges[k] + 1,
                                             boundary.data.begin() + edges[k + 1]);
                            if (type == 1) {
                                hp.points.push_back(edge.v());
                                hp.points.push_back(edge.v(11));
                            } else if (type == 2 || type == 3) {
                                Vec c = edge.v();
                                double a = edge.num(50) * pi / 180, b = edge.num(51) * pi / 180;
                                bool ccw = edge.num(73, 1) != 0;
                                double sweep = std::fmod(b - a + 2 * pi, 2 * pi);
                                if (!ccw)
                                    sweep -= 2 * pi;
                                if (sweep == 0)
                                    sweep = 2 * pi;
                                if (type == 2)
                                    arcPoints(hp.points, c, edge.num(40), edge.num(40), a, sweep);
                                else {
                                    Vec axis = edge.v(11);
                                    arcPoints(hp.points, c, length(axis),
                                              length(axis) * edge.num(40), a, sweep,
                                              std::atan2(axis.y, axis.x));
                                }
                            } else
                                warnings.insert("HATCH spline boundary unsupported");
                        }
                    }
                    pointCount += hp.points.size();
                    if (pointCount > lim.points || s.paths.size() >= lim.primitives)
                        throw Error("HATCH geometry limit exceeded");
                    for (auto &v : hp.points)
                        v = mat.apply(v);
                    if (!hp.points.empty())
                        s.paths.push_back(std::move(hp));
                }
                if (starts.size() > 2 || e.num(70) != 1)
                    warnings.insert("HATCH shown as boundaries: pattern/holes filling unsupported");
                continue;
            } else {
                if (e.type != "SEQEND" && e.type != "VERTEX")
                    warnings.insert("Unsupported DXF entity: " + e.type);
                continue;
            }
            pointCount += p.points.size();
            if (pointCount > lim.points || s.paths.size() + s.texts.size() >= lim.primitives)
                throw Error("Expanded DXF geometry limit exceeded");
            for (auto &v : p.points)
                v = mat.apply(v);
            if (!p.points.empty())
                s.paths.push_back(std::move(p));
        }
    };
    emit(model, {}, 0, 0, aci(7));
    s.warnings.assign(warnings.begin(), warnings.end());
    return s;
}
} // namespace cad
