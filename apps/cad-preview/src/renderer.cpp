#include "cad/windows.hpp"
#include <algorithm>
#include <limits>
namespace cad {
std::wstring wide(const std::string &s) {
    if (s.empty())
        return {};
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), int(s.size()), nullptr, 0);
    UINT cp = CP_UTF8;
    if (!n) {
        cp = 1252;
        n = MultiByteToWideChar(cp, 0, s.data(), int(s.size()), nullptr, 0);
    }
    std::wstring w(n, L' ');
    MultiByteToWideChar(cp, 0, s.data(), int(s.size()), w.data(), n);
    return w;
}
Image render(const Scene &s, int width, int height, const Camera &cam,
             const RenderControl &control) {
    const auto started = GetTickCount64();
    auto checkpoint = [&] {
        if (control.revision && control.revision->load() != control.expectedRevision)
            throw Error("Rendering cancelled");
        if (GetTickCount64() - started >= control.timeoutMs)
            throw Error("Rendering exceeded time limit; reduce the window size or use wireframe");
    };
    auto visible = [&](uint32_t layer) -> bool {
        if (control.visibleLayers && layer < control.visibleLayers->size())
            return (*control.visibleLayers)[layer];
        return layer >= s.layers.size() || s.layers[layer].visible;
    };
    checkpoint();
    if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
        throw Error("Render size out of range");
    Image im{width, height, {}};
    uint32_t bg = s.flat ? 0xff212830 : cam.dark ? 0xff202631 : 0xfff4f5f7;
    im.pixels.assign(size_t(width) * height, bg);
    if (!s.flat && cam.gradient) {
        for (int y = 0; y < height; ++y) {
            checkpoint();
            double position = double(y) / std::max(1, height - 1);
            double blend = std::pow(std::sin(3.14159265358979323846 * position), 2.);
            auto channel = [&](int edge, int middle) {
                return uint32_t(std::lround(edge + (middle - edge) * blend));
            };
            uint32_t color = 0xff000000u | (channel(252, 219) << 16) | (channel(253, 224) << 8) |
                             channel(255, 234);
            std::fill_n(im.pixels.begin() + size_t(y) * width, width, color);
        }
    }
    std::vector<double> depth(im.pixels.size(), -std::numeric_limits<double>::infinity());
    std::vector<float> depthSlope(im.pixels.size(), 0);
    Vec center = s.bounds.center();
    const double cy = std::cos(cam.yaw), sy = std::sin(cam.yaw);
    const double cp = std::cos(cam.pitch), sp = std::sin(cam.pitch);
    auto rotate = [&](Vec v) {
        if (s.flat)
            return v;
        double x = cy * v.x - sy * v.y, y = sy * v.x + cy * v.y;
        return Vec{x, cp * y - sp * v.z, sp * y + cp * v.z};
    };
    Bounds rb;
    for (int k = 0; k < 8; ++k)
        rb.add(rotate(Vec{k & 1 ? s.bounds.max.x : s.bounds.min.x,
                          k & 2 ? s.bounds.max.y : s.bounds.min.y,
                          k & 4 ? s.bounds.max.z : s.bounds.min.z} -
                      center));
    auto size = rb.size();
    double extent = std::max({size.x, size.y, 1e-20});
    double scale = .88 *
                   std::min(width / std::max(size.x, extent * 1e-6),
                            height / std::max(size.y, extent * 1e-6)) *
                   cam.zoom;
    if (!s.flat) {
        // A fixed bounding sphere keeps the camera scale independent of rotation.
        extent = std::max(length(s.bounds.size()), 1e-20);
        scale = .88 * std::min(width, height) / extent * cam.zoom;
    }
    auto project = [&](Vec v) {
        v = rotate(v - center);
        return Vec{width * .5 + v.x * scale + cam.panX, height * .5 - v.y * scale + cam.panY,
                   v.z / extent};
    };
    auto pack = [&](Color c, double light = 1.) {
        if (!s.flat && (!cam.dark || cam.gradient) && c.r > 220 && c.g > 220 && c.b > 220)
            c = {35, 45, 60, 255};
        return 0xff000000u | (uint32_t(std::clamp(c.r * light, 0., 255.)) << 16) |
               (uint32_t(std::clamp(c.g * light, 0., 255.)) << 8) |
               uint32_t(std::clamp(c.b * light, 0., 255.));
    };
    auto pixel = [&](int x, int y, double z, uint32_t c, bool edge = false, double slope = 0.) {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;
        auto i = size_t(y) * width + x;
        const double tolerance = edge ? 1e-5 + .75 * depthSlope[i] : 1e-7;
        if (z + tolerance >= depth[i]) {
            if (!edge) {
                depth[i] = z;
                depthSlope[i] = float(slope);
            }
            im.pixels[i] = c;
        }
    };
    auto line = [&](Vec a, Vec b, uint32_t color, int dash = 0) {
        checkpoint();
        double dx = b.x - a.x, dy = b.y - a.y;
        double lo = 0, hi = 1;
        auto clip = [&](double p, double q) {
            if (p == 0)
                return q >= 0;
            double t = q / p;
            if (p < 0)
                lo = std::max(lo, t);
            else
                hi = std::min(hi, t);
            return lo <= hi;
        };
        if (!clip(-dx, a.x) || !clip(dx, width - 1 - a.x) || !clip(-dy, a.y) ||
            !clip(dy, height - 1 - a.y))
            return;
        Vec start = a + (b - a) * lo, end = a + (b - a) * hi;
        int n = std::max(
            1, int(std::ceil(std::max(std::abs(end.x - start.x), std::abs(end.y - start.y)))));
        for (int j = 0; j <= n; ++j) {
            if (dash && (j / 5) % 2)
                continue;
            Vec p = start + (end - start) * (double(j) / n);
            pixel(int(std::round(p.x)), int(std::round(p.y)), p.z, color, true);
        }
    };
    for (auto &t : s.triangles) {
        checkpoint();
        Vec a = project(t.a), b = project(t.b), c = project(t.c);
        double area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        if (!cam.wire && std::abs(area) > 1e-15) {
            Vec n = cross(rotate(t.b - t.a), rotate(t.c - t.a));
            double light =
                .42 + .58 * std::abs(dot(n, Vec{.3, .4, .8660254})) / std::max(length(n), 1e-30);
            Color surfaceColor = !s.flat && cam.contrast ? Color{232, 174, 74, 255} : t.color;
            uint32_t col = pack(surfaceColor, light);
            double slope = (std::abs((b.z - a.z) * (c.y - a.y) - (c.z - a.z) * (b.y - a.y)) +
                            std::abs((b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z))) /
                           std::abs(area);
            int x0 = int(std::clamp(std::floor(std::min({a.x, b.x, c.x})), 0., double(width - 1))),
                x1 = int(std::clamp(std::ceil(std::max({a.x, b.x, c.x})), 0., double(width - 1))),
                y0 = int(std::clamp(std::floor(std::min({a.y, b.y, c.y})), 0., double(height - 1))),
                y1 = int(std::clamp(std::ceil(std::max({a.y, b.y, c.y})), 0., double(height - 1)));
            for (int y = y0; y <= y1; ++y) {
                checkpoint();
                for (int x = x0; x <= x1; ++x) {
                    double u = ((b.x - x) * (c.y - y) - (b.y - y) * (c.x - x)) / area,
                           v = ((c.x - x) * (a.y - y) - (c.y - y) * (a.x - x)) / area,
                           w = 1 - u - v;
                    if (u >= 0 && v >= 0 && w >= 0)
                        pixel(x, y, u * a.z + v * b.z + w * c.z, col, false, slope);
                }
            }
        }
    }
    // Draw mesh wireframe after all faces, so later faces cannot overwrite visible lines.
    if (cam.wire && (s.format == "STL" || s.paths.empty()))
        for (auto &t : s.triangles) {
            Vec a = project(t.a), b = project(t.b), c = project(t.c);
            uint32_t col = cam.dark ? 0xffbac8dc : 0xff263244;
            line(a, b, col);
            line(b, c, col);
            line(c, a, col);
        }
    for (auto &p : s.paths) {
        if (!s.flat && !cam.edges && !cam.wire)
            continue;
        if (!visible(p.layer))
            continue;
        uint32_t col = s.flat || s.triangles.empty() ? pack(p.color)
                       : cam.wire                    ? 0xff8298b5
                                                     : 0xff253344;
        for (size_t j = 1; j < p.points.size(); ++j)
            line(project(p.points[j - 1]), project(p.points[j]), col, p.lineType);
        if (p.closed && p.points.size() > 1)
            line(project(p.points.back()), project(p.points.front()), col);
    }
    // GDI text uses the same DIB and projection as geometry.
    struct Surface {
        HDC dc = CreateCompatibleDC(nullptr);
        HBITMAP bmp = nullptr;
        HGDIOBJ old = nullptr;
        ~Surface() {
            if (old)
                SelectObject(dc, old);
            if (bmp)
                DeleteObject(bmp);
            if (dc)
                DeleteDC(dc);
        }
    } surface;
    if (!surface.dc)
        throw Error("Cannot create rendering context");
    HDC dc = surface.dc;
    HBITMAP bmp = surface.bmp = bitmap(im);
    auto old = SelectObject(dc, bmp);
    surface.old = old;
    SetBkMode(dc, TRANSPARENT);
    if (s.flat)
        for (auto &p : s.paths) {
            checkpoint();
            if (!p.filled || p.points.size() < 3 || !visible(p.layer))
                continue;
            if (p.points.size() > 200000)
                throw Error("Filled polygon exceeds display complexity limit");
            std::vector<POINT> points;
            for (auto v : p.points) {
                auto q = project(v);
                points.push_back(
                    {LONG(std::clamp(q.x, -1e7, 1e7)), LONG(std::clamp(q.y, -1e7, 1e7))});
            }
            auto col = pack(p.color);
            auto brush = CreateSolidBrush(RGB((col >> 16) & 255, (col >> 8) & 255, col & 255));
            auto ob = SelectObject(dc, brush);
            auto op = SelectObject(dc, GetStockObject(NULL_PEN));
            Polygon(dc, points.data(), int(points.size()));
            SelectObject(dc, ob);
            SelectObject(dc, op);
            DeleteObject(brush);
        }
    for (auto &t : s.texts) {
        checkpoint();
        if (!visible(t.layer))
            continue;
        auto p = project(t.position);
        int h = int(std::clamp(t.height * scale, 1., 512.));
        auto w = wide(t.value.substr(0, 16384));
        HFONT font = CreateFontW(-h, 0, int(std::fmod(t.angle, 360.) * 10),
                                 int(std::fmod(t.angle, 360.) * 10), FW_NORMAL, FALSE, FALSE, FALSE,
                                 DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                                 ANTIALIASED_QUALITY, DEFAULT_PITCH, L"Segoe UI");
        auto of = SelectObject(dc, font);
        SetTextColor(dc, RGB(t.color.r, t.color.g, t.color.b));
        TextOutW(dc, int(std::clamp(p.x, -1e6, 1e6)), int(std::clamp(p.y - h, -1e6, 1e6)), w.data(),
                 int(w.size()));
        SelectObject(dc, of);
        DeleteObject(font);
    }
    SelectObject(dc, old);
    surface.old = nullptr;
    BITMAPINFO bi{};
    bi.bmiHeader = {sizeof(BITMAPINFOHEADER), width, -height, 1, 32, BI_RGB};
    GetDIBits(dc, bmp, 0, height, im.pixels.data(), &bi, DIB_RGB_COLORS);
    for (auto &v : im.pixels)
        v |= 0xff000000;
    checkpoint();
    return im;
}
HBITMAP bitmap(const Image &im) {
    BITMAPINFO bi{};
    bi.bmiHeader = {sizeof(BITMAPINFOHEADER), im.width, -im.height, 1, 32, BI_RGB};
    void *bits = nullptr;
    HBITMAP b = CreateDIBSection(nullptr, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (!b)
        throw Error("Cannot allocate thumbnail bitmap");
    std::copy(im.pixels.begin(), im.pixels.end(), static_cast<uint32_t *>(bits));
    return b;
}
} // namespace cad
