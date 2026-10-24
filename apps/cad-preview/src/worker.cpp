#include "cad/windows.hpp"
#include <fstream>
#include <windows.h>
int wmain(int argc, wchar_t **argv) {
    if (argc != 3 && argc != 4)
        return 2;
    try {
        auto scene = cad::load(argv[1]);
        if (argc == 4) {
            int size = std::stoi(argv[3]);
            if (size < 1 || size > 1024)
                throw cad::Error("Invalid thumbnail size");
            auto im = cad::render(scene, size, size, {});
            std::ofstream f(argv[2], std::ios::binary);
            f.write(reinterpret_cast<const char *>(im.pixels.data()), im.pixels.size() * 4);
            if (!f)
                throw cad::Error("Cannot write thumbnail");
        } else
            cad::writeScene(argv[2], scene);
        return 0;
    } catch (const std::exception &e) {
        std::ofstream err(std::filesystem::path(argv[2]).concat(L".error"), std::ios::binary);
        err << e.what();
        return 1;
    } catch (...) {
        return 3;
    }
}
