#include "cad/windows.hpp"
#include <fstream>
namespace cad {
Camera loadSettings() {
    Camera camera;
    try {
        std::ifstream f(temporaryRoot().parent_path() / L"settings.ini");
        std::string key;
        int value;
        bool currentStyle = false;
        while (f >> key >> value) {
            if (key == "dark")
                camera.dark = value != 0;
            else if (key == "edges")
                camera.edges = value != 0;
            else if (key == "wire")
                camera.wire = value != 0;
            else if (key == "help")
                camera.help = value != 0;
            else if (key == "contrast")
                camera.contrast = value != 0;
            else if (key == "gradient")
                camera.gradient = value != 0;
            else if (key == "styleVersion")
                currentStyle = value >= 1;
        }
        if (!currentStyle)
            camera.edges = true;
    } catch (...) {
    }
    return camera;
}
void saveSettings(const Camera &camera) {
    try {
        auto p = temporaryRoot().parent_path() / L"settings.ini";
        auto tmp = p;
        tmp += L".tmp";
        {
            std::ofstream f(tmp);
            f << "dark " << int(camera.dark) << "\nedges " << int(camera.edges) << "\nwire "
              << int(camera.wire) << "\nhelp " << int(camera.help) << "\ncontrast "
              << int(camera.contrast) << "\ngradient " << int(camera.gradient)
              << "\nstyleVersion 1\n";
            if (!f)
                return;
        }
        MoveFileExW(tmp.c_str(), p.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
    } catch (...) {
    }
}
} // namespace cad
