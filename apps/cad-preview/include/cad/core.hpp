#pragma once
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <stdexcept>
#include <string>
#include <vector>

namespace cad {
struct Vec {
    double x = 0, y = 0, z = 0;
    Vec operator+(Vec b) const { return {x + b.x, y + b.y, z + b.z}; }
    Vec operator-(Vec b) const { return {x - b.x, y - b.y, z - b.z}; }
    Vec operator*(double s) const { return {x * s, y * s, z * s}; }
};
inline Vec cross(Vec a, Vec b) {
    return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}
inline double dot(Vec a, Vec b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}
inline double length(Vec a) {
    return std::sqrt(dot(a, a));
}
struct Color {
    uint8_t r = 210, g = 215, b = 225, a = 255;
};
struct Bounds {
    Vec min{}, max{};
    bool empty = true;
    void add(Vec);
    Vec center() const { return (min + max) * .5; }
    Vec size() const { return max - min; }
};
struct Layer {
    std::string name;
    Color color;
    bool visible = true;
};
struct Path {
    std::vector<Vec> points;
    Color color;
    uint32_t layer = 0;
    bool closed = false, filled = false;
    int lineType = 0;
};
struct Triangle {
    Vec a, b, c;
    Color color;
};
struct Text {
    Vec position;
    std::string value;
    double height = 1, angle = 0;
    uint32_t layer = 0;
    Color color;
};
struct Scene {
    bool flat = false;
    Bounds bounds;
    std::vector<Layer> layers;
    std::vector<Path> paths;
    std::vector<Triangle> triangles;
    std::vector<Text> texts;
    std::vector<std::string> warnings;
    std::string format, units = "unspecified";
    void finalize();
};
struct Limits {
    uint64_t fileBytes = 256ull * 1024 * 1024;
    uint64_t primitives = 2000000;
    uint64_t points = 12000000;
    unsigned blockDepth = 32;
    double deflection = .1;
};
struct Error : std::runtime_error {
    using std::runtime_error::runtime_error;
};
Scene load(const std::filesystem::path &, const Limits & = {});
Scene loadDxf(const std::filesystem::path &, const Limits &);
Scene loadStl(const std::filesystem::path &, const Limits &);
Scene loadStep(const std::filesystem::path &, const Limits &);
void writeScene(const std::filesystem::path &, const Scene &);
Scene readScene(const std::filesystem::path &, const Limits & = {});
std::string readFile(const std::filesystem::path &, const Limits &);
} // namespace cad
