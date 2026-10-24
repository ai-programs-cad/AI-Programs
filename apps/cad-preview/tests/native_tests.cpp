#include "cad/help_text.hpp"
#include "cad/windows.hpp"
#include <algorithm>
#include <fstream>
#include <iostream>
#include <shlwapi.h>
#include <shobjidl.h>
#include <thumbcache.h>
namespace {
int checks = 0;
void test(bool ok, const char *what) {
    ++checks;
    if (!ok)
        throw cad::Error(what);
}
void hr(HRESULT h, const char *w) {
    test(SUCCEEDED(h), w);
}
void save(const cad::Image &im, const std::filesystem::path &p) {
    BITMAPFILEHEADER bf{};
    bf.bfType = 0x4d42;
    bf.bfOffBits = sizeof(bf) + sizeof(BITMAPINFOHEADER);
    bf.bfSize = bf.bfOffBits + DWORD(im.pixels.size() * 4);
    BITMAPINFOHEADER bi{sizeof(bi), im.width, -im.height, 1, 32, BI_RGB};
    std::ofstream f(p, std::ios::binary);
    f.write(reinterpret_cast<char *>(&bf), sizeof(bf));
    f.write(reinterpret_cast<char *>(&bi), sizeof(bi));
    f.write(reinterpret_cast<const char *>(im.pixels.data()), im.pixels.size() * 4);
}
cad::Image captureWindow(HWND window) {
    RECT r{};
    GetClientRect(window, &r);
    cad::Image image{r.right, r.bottom, {}};
    image.pixels.assign(size_t(r.right) * r.bottom, 0xff00ff00);
    auto bitmap = cad::bitmap(image);
    HDC dc = CreateCompatibleDC(nullptr);
    auto old = SelectObject(dc, bitmap);
    SendMessageW(window, WM_PRINT, reinterpret_cast<WPARAM>(dc),
                 PRF_CLIENT | PRF_ERASEBKGND | PRF_CHILDREN);
    SelectObject(dc, old);
    BITMAPINFO bi{};
    bi.bmiHeader = {sizeof(BITMAPINFOHEADER), r.right, -r.bottom, 1, 32, BI_RGB};
    auto rows = GetDIBits(dc, bitmap, 0, r.bottom, image.pixels.data(), &bi, DIB_RGB_COLORS);
    DeleteObject(bitmap);
    DeleteDC(dc);
    if (rows != UINT(r.bottom))
        throw cad::Error("Cannot capture preview test frame");
    return image;
}
} // namespace
int main(int argc, char **argv) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    try {
        auto root = std::filesystem::absolute(argv[1]);
        test(std::wstring(cad::helpText().title) == L"Controls", "Controls help is English only");
        test(std::wstring(cad::helpText().show) == L"Show controls" &&
                 std::wstring(cad::helpText().hide) == L"Hide -",
             "Help buttons are English only");
        auto temp = cad::moduleDirectory() / L"test-temp" /
                    (L"run-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
        SetEnvironmentVariableW(L"CADPREVIEW_TEMP_DIRECTORY", temp.c_str());
        auto mod = LoadLibraryW((cad::moduleDirectory() / L"CADPreview.Shell.dll").c_str());
        test(mod != nullptr, "Load Shell DLL");
        auto get = reinterpret_cast<HRESULT(__stdcall *)(REFCLSID, REFIID, void **)>(
            GetProcAddress(mod, "DllGetClassObject"));
        auto unload =
            reinterpret_cast<HRESULT(__stdcall *)()>(GetProcAddress(mod, "DllCanUnloadNow"));
        test(get && unload, "COM exports");
        CLSID cls;
        CLSIDFromString(L"{70D346A5-7204-45EE-A827-7501B84494E1}", &cls);
        IClassFactory *factory = nullptr;
        hr(get(cls, IID_PPV_ARGS(&factory)), "Thumbnail factory");
        for (auto name : {"geometry.dxf", "tetrahedron-ascii.stl", "tetrahedron-binary.stl",
                          "assembly.step", "assembly.stp"}) {
            auto file = root / name;
            if (!std::filesystem::exists(file))
                continue;
            auto before = cad::readFile(file, {});
            for (unsigned size : {32, 64, 128, 256}) {
                IInitializeWithFile *init = nullptr;
                hr(factory->CreateInstance(nullptr, IID_PPV_ARGS(&init)),
                   "Create thumbnail provider");
                hr(init->Initialize(file.c_str(), STGM_READ), "Initialize thumbnail");
                IThumbnailProvider *thumb = nullptr;
                hr(init->QueryInterface(IID_PPV_ARGS(&thumb)), "Query thumbnail interface");
                HBITMAP bmp = nullptr;
                WTS_ALPHATYPE alpha;
                hr(thumb->GetThumbnail(size, &bmp, &alpha), "Get thumbnail");
                BITMAP info{};
                GetObjectW(bmp, sizeof(info), &info);
                test(info.bmWidth == int(size) && info.bmHeight == int(size), "Thumbnail size");
                DeleteObject(bmp);
                thumb->Release();
                init->Release();
            }
            test(before == cad::readFile(file, {}), "Source file changed");
        }
        factory->Release();
        test(unload() == S_OK, "Thumbnail COM lifetime leak");
        // Stream-based initialization is the path used by isolated Windows thumbnails.
        hr(get(cls, IID_PPV_ARGS(&factory)), "Stream factory");
        IInitializeWithStream *streamInit = nullptr;
        hr(factory->CreateInstance(nullptr, IID_PPV_ARGS(&streamInit)), "Stream provider");
        auto bytes = cad::readFile(root / "geometry.dxf", {});
        auto stream =
            SHCreateMemStream(reinterpret_cast<const BYTE *>(bytes.data()), UINT(bytes.size()));
        hr(streamInit->Initialize(stream, STGM_READ), "Stream initialization");
        stream->Release();
        IThumbnailProvider *thumb = nullptr;
        hr(streamInit->QueryInterface(IID_PPV_ARGS(&thumb)), "Stream QI");
        HBITMAP bmp = nullptr;
        WTS_ALPHATYPE alpha;
        hr(thumb->GetThumbnail(128, &bmp, &alpha), "Stream thumbnail");
        DeleteObject(bmp);
        thumb->Release();
        streamInit->Release();
        factory->Release();
        test(unload() == S_OK, "Stream COM lifetime leak");
        CLSIDFromString(L"{62564304-3EA6-4B9E-970E-452A8ACBC5B3}", &cls);
        hr(get(cls, IID_PPV_ARGS(&factory)), "Preview factory");
        auto host = CreateWindowExW(0, L"STATIC", L"CAD Preview tests", WS_OVERLAPPED, 0, 0, 640,
                                    480, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        test(host != nullptr, "Preview host");
        // Explorer may retain the COM object and select a new file after Unload.
        IInitializeWithStream *reusedStream = nullptr;
        hr(factory->CreateInstance(nullptr, IID_PPV_ARGS(&reusedStream)),
           "Reusable preview instance");
        IInitializeWithFile *reusedFile = nullptr;
        IPreviewHandler *reusedPreview = nullptr;
        hr(reusedStream->QueryInterface(IID_PPV_ARGS(&reusedFile)), "Reusable file initializer");
        hr(reusedStream->QueryInterface(IID_PPV_ARGS(&reusedPreview)),
           "Reusable preview interface");
        cad::Image firstDxf;
        const char *selections[] = {"geometry.dxf",          "large-drawing.dxf", "geometry.dxf",
                                    "tetrahedron-ascii.stl", "geometry.dxf",      "assembly.step",
                                    "geometry.dxf",          "large-drawing.dxf"};
        for (int selection = 0; selection < 8; ++selection) {
            RECT area{0, 0, 640, 480};
            hr(reusedPreview->SetWindow(host, &area), "Set parent before reinitialization");
            auto path = root / selections[selection];
            auto contents = cad::readFile(path, {});
            auto input = SHCreateMemStream(reinterpret_cast<const BYTE *>(contents.data()),
                                           UINT(contents.size()));
            if (selection % 3 == 0)
                hr(reusedFile->Initialize(path.c_str(), STGM_READ),
                   "First-click file reinitialization");
            else
                hr(reusedStream->Initialize(input, STGM_READ),
                   "First-click stream reinitialization");
            test(reusedStream->Initialize(input, STGM_READ) ==
                     HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED),
                 "Active preview still rejects duplicate initialization");
            input->Release();
            hr(reusedPreview->DoPreview(), "First-click DoPreview after file change");
            auto child = FindWindowExW(host, nullptr, L"CADPreviewViewport03", nullptr);
            test(child != nullptr, "Reused handler creates a preview window");
            auto until = GetTickCount64() + 10000;
            LRESULT state = 0;
            while (GetTickCount64() < until && !state) {
                MSG event;
                while (PeekMessageW(&event, nullptr, 0, 0, PM_REMOVE)) {
                    TranslateMessage(&event);
                    DispatchMessageW(&event);
                }
                state = SendMessageW(child, WM_APP + 72, 0, 0);
                Sleep(2);
            }
            test(state == 1, "Changed selection produces a frame without a second initialization");
            auto current = captureWindow(child);
            if (!selection)
                firstDxf = current;
            else if (selection % 2 == 0)
                test(current.pixels == firstDxf.pixels,
                     "Returning to DXF shows the correct drawing");
            else
                test(current.pixels != firstDxf.pixels,
                     "Switching files does not retain the old drawing");
            hr(reusedPreview->Unload(), "Unload selection for handler reuse");
            test(!FindWindowExW(host, nullptr, L"CADPreviewViewport03", nullptr),
                 "Unload destroys the previous viewport");
            hr(reusedPreview->Unload(), "Repeated Unload is safe");
        }
        for (int selection = 0; selection < 4; ++selection) {
            auto contents =
                cad::readFile(root / (selection % 2 ? "assembly.step" : "geometry.dxf"), {});
            auto input = SHCreateMemStream(reinterpret_cast<const BYTE *>(contents.data()),
                                           UINT(contents.size()));
            hr(reusedStream->Initialize(input, STGM_READ), "Rapid selection initialization");
            input->Release();
            hr(reusedPreview->DoPreview(), "Rapid selection starts preview");
            hr(reusedPreview->Unload(), "Rapid selection cancels loading safely");
        }
        bool stagedFilesRemain = false;
        for (const auto &entry : std::filesystem::directory_iterator(temp))
            stagedFilesRemain |= entry.path().filename().wstring().find(L"CADPreviewStream-") == 0;
        test(!stagedFilesRemain, "Unload removes staged inputs between file selections");
        reusedPreview->Release();
        reusedFile->Release();
        reusedStream->Release();
        for (auto name : {"geometry.dxf", "tetrahedron-ascii.stl", "assembly.step", "assembly.stp",
                          "corrupt.stl"}) {
            if (!std::filesystem::exists(root / name))
                continue;
            IInitializeWithStream *init = nullptr;
            hr(factory->CreateInstance(nullptr, IID_PPV_ARGS(&init)), "Create preview");
            auto contents = cad::readFile(root / name, {});
            auto input = SHCreateMemStream(reinterpret_cast<const BYTE *>(contents.data()),
                                           UINT(contents.size()));
            test(input != nullptr, "Create preview input stream");
            hr(init->Initialize(input, STGM_READ), "Preview stream initialization");
            input->Release();
            IPreviewHandler *prev = nullptr;
            hr(init->QueryInterface(IID_PPV_ARGS(&prev)), "Query preview");
            RECT r{0, 0, 640, 480};
            hr(prev->SetWindow(host, &r), "Preview parent");
            hr(prev->DoPreview(), "DoPreview");
            auto child = FindWindowExW(host, nullptr, L"CADPreviewViewport03", nullptr);
            test(child != nullptr, "Preview child window");
            auto end = GetTickCount64() + 10000;
            LRESULT state = 0;
            while (GetTickCount64() < end) {
                MSG msg;
                while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
                state = SendMessageW(child, WM_APP + 72, 0, 0);
                if (state)
                    break;
                Sleep(5);
            }
            test(state == (std::string(name) == "corrupt.stl" ? -1 : 1),
                 "Asynchronous preview completion");
            if (std::string(name) != "corrupt.stl") {
                SendMessageW(child, WM_COMMAND, 12, 0);
                auto info = GetDlgItem(child, 201);
                test(info && (GetWindowLongPtrW(info, GWL_STYLE) & WS_VISIBLE),
                     "File information appears inside the preview pane");
                std::wstring details(GetWindowTextLengthW(info) + 1, L'\0');
                GetWindowTextW(info, details.data(), int(details.size()));
                test(details.find(L"Format:") != std::wstring::npos &&
                         details.find(L"File size:") != std::wstring::npos &&
                         details.find(L"Dimensions:") != std::wstring::npos,
                     "Information contains file size, format and dimensions");
                test((details.find(L"Triangles:") == std::wstring::npos) ==
                         (std::string(name) == "geometry.dxf"),
                     "DXF information omits irrelevant triangle statistics");
                SendMessageW(child, WM_COMMAND, 16, 0);
                test(!GetDlgItem(child, 201), "Information close button removes the panel");
                auto home = captureWindow(child);
                test((GetClassLongPtrW(child, GCL_STYLE) & CS_DBLCLKS) != 0,
                     "Viewport requests native double click events");
                const auto previous = SendMessageW(child, WM_APP + 74, 0, 0);
                for (int i = 0; i < 20; ++i)
                    SendMessageW(child, WM_MOUSEWHEEL, MAKEWPARAM(0, 120), 0);
                const auto until = GetTickCount64() + 5000;
                while (GetTickCount64() < until &&
                       SendMessageW(child, WM_APP + 74, 0, 0) <= previous) {
                    MSG msg;
                    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
                        DispatchMessageW(&msg);
                    Sleep(5);
                }
                test(SendMessageW(child, WM_APP + 74, 0, 0) > previous,
                     "Latest zoom frame completion");
                SendMessageW(child, WM_MBUTTONDOWN, MK_MBUTTON, MAKELPARAM(30, 30));
                SendMessageW(child, WM_MOUSEMOVE, MK_MBUTTON, MAKELPARAM(90, 70));
                SendMessageW(child, WM_MBUTTONUP, 0, MAKELPARAM(90, 70));
                SendMessageW(child, WM_MBUTTONDBLCLK, MK_MBUTTON, MAKELPARAM(90, 70));
                auto fitDeadline = GetTickCount64() + 5000;
                while (GetTickCount64() < fitDeadline) {
                    MSG msg;
                    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
                        DispatchMessageW(&msg);
                    if (SendMessageW(child, WM_APP + 74, 0, 0) ==
                        SendMessageW(child, WM_APP + 75, 0, 0))
                        break;
                    Sleep(2);
                }
                test(captureWindow(child).pixels == home.pixels,
                     "Middle double click fits DXF/3D after zoom and pan");
                RECT wideRect{0, 0, 2000, 400};
                const auto oldRevision = SendMessageW(child, WM_APP + 74, 0, 0);
                hr(prev->SetRect(&wideRect), "Wide preview resize");
                auto deadline = GetTickCount64() + 5000;
                while (GetTickCount64() < deadline) {
                    MSG msg;
                    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
                        DispatchMessageW(&msg);
                    auto currentDimensions = SendMessageW(child, WM_APP + 77, 0, 0);
                    if (LOWORD(currentDimensions) == 1600 && HIWORD(currentDimensions) == 320 &&
                        SendMessageW(child, WM_APP + 74, 0, 0) > oldRevision &&
                        SendMessageW(child, WM_APP + 74, 0, 0) ==
                            SendMessageW(child, WM_APP + 75, 0, 0))
                        break;
                    Sleep(5);
                }
                auto dimensions = SendMessageW(child, WM_APP + 77, 0, 0);
                test(LOWORD(dimensions) == 1600 && HIWORD(dimensions) == 320,
                     "Wide viewport preserves render aspect ratio");
            }
            hr(prev->Unload(), "Unload preview");
            prev->Release();
            init->Release();
        }
        DestroyWindow(host);
        factory->Release();
        test(unload() == S_OK, "Preview COM lifetime leak");
        FreeLibrary(mod);
        auto s = cad::importIsolated(root / "tetrahedron-binary.stl");
        test(s.triangles.size() == 4, "Isolated worker IPC");
        cad::Camera camera;
        auto image = cad::render(s, 512, 512, camera);
        size_t nonBackground = 0;
        for (int y = 0; y < image.height; ++y)
            for (int x = 0; x < image.width; ++x)
                nonBackground += image.pixels[y * image.width + x] != image.pixels[y * image.width];
        test(nonBackground > 1000, "Renderer produced blank model");
        save(image, cad::moduleDirectory() / "stl-validation.bmp");
        if (std::filesystem::exists(root / "assembly.step")) {
            auto assembly = cad::importIsolated(root / "assembly.step");
            cad::Camera c;
            c.edges = true;
            save(cad::render(assembly, 800, 512, c),
                 cad::moduleDirectory() / "step-validation.bmp");
        }
        auto d = cad::importIsolated(root / "geometry.dxf");
        save(cad::render(d, 800, 512, {}), cad::moduleDirectory() / "dxf-validation.bmp");
        bool rejected = false;
        try {
            cad::importIsolated(root / "corrupt.stl");
        } catch (...) {
            rejected = true;
        }
        test(rejected, "Worker accepted corrupt STL");
        std::atomic_bool cancel{true};
        bool cancelled = false;
        try {
            cad::importIsolated(root / "tetrahedron-ascii.stl", 30000, &cancel);
        } catch (...) {
            cancelled = true;
        }
        test(cancelled, "Worker cancellation ignored");
        std::atomic<uint64_t> revision{0};
        cad::RenderControl control{&revision, 1, nullptr, 5000};
        bool stopped = false;
        try {
            cad::render(s, 512, 512, camera, control);
        } catch (const cad::Error &e) {
            stopped = std::string(e.what()) == "Rendering cancelled";
        }
        test(stopped, "Stale render must cancel before work");
        cad::Scene busy = s;
        busy.triangles.assign(20000, s.triangles.front());
        revision = 0;
        control.expectedRevision = 0;
        auto started = GetTickCount64();
        std::thread interrupt([&] {
            Sleep(20);
            revision = 1;
        });
        stopped = false;
        try {
            cad::render(busy, 1600, 1600, camera, control);
        } catch (const cad::Error &e) {
            stopped = std::string(e.what()) == "Rendering cancelled";
        }
        interrupt.join();
        test(stopped, "Active heavy render cancellation");
        test(GetTickCount64() - started < 2000, "Render cancellation latency");
        control = {nullptr, 0, nullptr, 0};
        stopped = false;
        try {
            cad::render(s, 512, 512, camera, control);
        } catch (const cad::Error &e) {
            stopped = std::string(e.what()).find("time limit") != std::string::npos;
        }
        test(stopped, "Frame time budget enforced");
        cad::Camera altered = camera;
        altered.contrast = false;
        auto originalColors = cad::render(s, 512, 512, altered);
        test(originalColors.pixels != image.pixels, "Contrast style differs from source colors");
        auto snapshot = s.triangles.front().color;
        test(snapshot.r == 165 && snapshot.g == 190 && snapshot.b == 220,
             "Display palette leaves imported STL colors unchanged");
        altered = camera;
        altered.edges = false;
        test(cad::render(s, 512, 512, altered).pixels != image.pixels,
             "Feature edges can be disabled independently of contrast color");
        save(image, cad::moduleDirectory() / "contrast-stl-validation.bmp");
        cad::Scene occlusion;
        occlusion.format = "STEP";
        occlusion.triangles.push_back({{0, 0, 0}, {10, 0, 0}, {10, 10, 0}, {}});
        occlusion.triangles.push_back({{0, 0, 0}, {10, 10, 0}, {0, 10, 0}, {}});
        cad::Path hiddenEdge;
        hiddenEdge.points = {{2, 5, -1}, {8, 5, -1}};
        occlusion.paths.push_back(hiddenEdge);
        occlusion.finalize();
        auto frontCamera = camera;
        frontCamera.yaw = frontCamera.pitch = 0;
        auto hiddenImage = cad::render(occlusion, 256, 256, frontCamera);
        auto edgesOff = frontCamera;
        edgesOff.edges = false;
        test(hiddenImage.pixels == cad::render(occlusion, 256, 256, edgesOff).pixels,
             "Hidden feature edge does not show through an opaque face");
        for (auto &point : occlusion.paths.front().points)
            point.z = 0;
        test(cad::render(occlusion, 256, 256, frontCamera).pixels !=
                 cad::render(occlusion, 256, 256, edgesOff).pixels,
             "Visible coplanar feature edge is drawn over a face");
        if (std::filesystem::exists(root / "assembly.step")) {
            auto coloredStep = cad::load(root / "assembly.step");
            save(cad::render(coloredStep, 800, 600, camera),
                 cad::moduleDirectory() / "contrast-step-validation.bmp");
            altered = camera;
            altered.contrast = false;
            save(cad::render(coloredStep, 800, 600, altered),
                 cad::moduleDirectory() / "source-step-validation.bmp");
        }
        altered = camera;
        altered.yaw += .45;
        test(cad::render(s, 512, 512, altered).pixels != image.pixels,
             "Rotation changes the rendered model");
        altered = camera;
        altered.zoom = 1.5;
        test(cad::render(s, 512, 512, altered).pixels != image.pixels,
             "Zoom changes the rendered model");
        auto first = cad::render(d, 512, 512, camera);
        std::vector<bool> hidden(d.layers.size(), false);
        control = {nullptr, 0, &hidden, 10000};
        auto empty = cad::render(d, 512, 512, camera, control);
        test(first.pixels != empty.pixels, "Layer visibility snapshot applies");
        test(d.layers.front().visible, "Render snapshot must not mutate source layers");
        cad::Scene overComplex;
        overComplex.flat = true;
        overComplex.layers.push_back({"Fill", {}});
        cad::Path polygon;
        polygon.filled = true;
        polygon.points.assign(200001, {0, 0, 0});
        overComplex.paths.push_back(std::move(polygon));
        overComplex.finalize();
        auto handles = GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS);
        for (int attempt = 0; attempt < 3; ++attempt) {
            stopped = false;
            try {
                cad::render(overComplex, 128, 128, camera);
            } catch (const cad::Error &e) {
                stopped = std::string(e.what()).find("complexity limit") != std::string::npos;
            }
            test(stopped, "Excessive filled polygon fails safely");
        }
        test(GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS) == handles,
             "Failed GDI render releases bitmap and context");
        std::atomic_bool windowsOk{true};
        std::vector<std::thread> creators;
        for (int k = 0; k < 3; ++k)
            creators.emplace_back([&] {
                try {
                    for (int j = 0; j < 15; ++j) {
                        auto parent = CreateWindowExW(0, L"STATIC", L"Lifecycle test",
                                                      WS_OVERLAPPED, 0, 0, 300, 200, nullptr,
                                                      nullptr, GetModuleHandleW(nullptr), nullptr);
                        if (!parent)
                            throw cad::Error("Cannot create lifecycle host");
                        {
                            cad::Viewport preview(parent);
                        }
                        DestroyWindow(parent);
                    }
                } catch (...) {
                    windowsOk = false;
                }
            });
        for (auto &creator : creators)
            creator.join();
        test(windowsOk, "Concurrent viewport window class lifetime");
        // A vertical reference edge must keep its apparent length when only yaw changes.
        cad::Scene reference;
        reference.layers.push_back({"Reference", {}});
        reference.bounds.add({-20, -1, -2});
        reference.bounds.add({20, 1, 2});
        cad::Path edge;
        edge.color = {255, 0, 0, 255};
        edge.points = {{0, 0, -2}, {0, 0, 2}};
        reference.paths.push_back(edge);
        cad::Camera referenceCamera;
        referenceCamera.edges = true;
        referenceCamera.pitch = 1.57079632679;
        auto edgeHeight = [&](double yaw) {
            referenceCamera.yaw = yaw;
            auto im = cad::render(reference, 512, 512, referenceCamera);
            int lo = 512, hi = -1;
            for (int y = 0; y < 512; ++y)
                for (int x = 0; x < 512; ++x)
                    if ((im.pixels[y * 512 + x] & 0xffffff) == 0xff0000) {
                        lo = std::min(lo, y);
                        hi = std::max(hi, y);
                    }
            return hi - lo;
        };
        test(edgeHeight(0) > 20, "Reference edge is visible for scale test");
        test(std::abs(edgeHeight(0) - edgeHeight(1.57079632679)) <= 1,
             "Rotation must not trigger automatic camera rescaling");
        auto motionHost =
            CreateWindowExW(0, L"STATIC", L"Motion regression", WS_OVERLAPPED, 0, 0, 800, 600,
                            nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        test(motionHost != nullptr, "Motion test host");
        {
            cad::Viewport viewport(motionHost);
            RECT rect{0, 0, 800, 600};
            viewport.resize(rect);
            viewport.open(argc > 2 ? std::filesystem::absolute(argv[2]) : root / "large-mesh.stl");
            auto pump = [] {
                MSG msg;
                while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            };
            auto until = GetTickCount64() + 10000;
            while (GetTickCount64() < until &&
                   SendMessageW(viewport.hwnd(), WM_APP + 72, 0, 0) != 1) {
                pump();
                Sleep(2);
            }
            test(SendMessageW(viewport.hwnd(), WM_APP + 72, 0, 0) == 1, "Motion initial frame");
            auto before = SendMessageW(viewport.hwnd(), WM_APP + 74, 0, 0), lastRevision = before;
            unsigned frames = 0;
            SendMessageW(viewport.hwnd(), WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(200, 200));
            auto began = GetTickCount64();
            while (GetTickCount64() - began < 700) {
                auto elapsed = GetTickCount64() - began;
                SendMessageW(viewport.hwnd(), WM_MOUSEMOVE, MK_LBUTTON,
                             MAKELPARAM(200 + elapsed / 4, 200 + elapsed / 10));
                pump();
                auto current = SendMessageW(viewport.hwnd(), WM_APP + 74, 0, 0);
                if (current > lastRevision) {
                    ++frames;
                    lastRevision = current;
                }
                Sleep(2);
            }
            std::cout << "Continuous drag: " << frames << " completed frames in 700 ms\n";
            test(frames >= 3, "Continuous mouse input must not starve completed frames");
            SendMessageW(viewport.hwnd(), WM_LBUTTONUP, 0, MAKELPARAM(375, 270));
            until = GetTickCount64() + 10000;
            while (GetTickCount64() < until) {
                pump();
                if (SendMessageW(viewport.hwnd(), WM_APP + 74, 0, 0) ==
                    SendMessageW(viewport.hwnd(), WM_APP + 75, 0, 0))
                    break;
                Sleep(2);
            }
            auto dimensions = SendMessageW(viewport.hwnd(), WM_APP + 77, 0, 0);
            test(LOWORD(dimensions) == 800 && HIWORD(dimensions) == 600,
                 "Full quality restored after drag ends");
            auto rotatedHome = captureWindow(viewport.hwnd());
            auto originalHelp = cad::loadSettings().help;
            SendMessageW(viewport.hwnd(), WM_KEYDOWN, VK_F1, 0);
            test(captureWindow(viewport.hwnd()).pixels == rotatedHome.pixels,
                 "F1 is not a CAD preview shortcut");
            viewport.command(13);
            auto toggledHelp = captureWindow(viewport.hwnd());
            test(toggledHelp.pixels != rotatedHome.pixels,
                 "Help command changes the visible overlay");
            test(cad::loadSettings().help != originalHelp, "Help visibility is saved immediately");
            int dpi = GetDpiForWindow(viewport.hwnd());
            int header = MulDiv(36, dpi, 96), pad = MulDiv(12, dpi, 96);
            int height = originalHelp ? header : MulDiv(216, dpi, 96);
            int buttonY = 600 - height - pad + header / 2;
            SendMessageW(viewport.hwnd(), WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(2 * pad, buttonY));
            SendMessageW(viewport.hwnd(), WM_LBUTTONUP, 0, MAKELPARAM(2 * pad, buttonY));
            test(cad::loadSettings().help == originalHelp, "Click restores saved help visibility");
            test(captureWindow(viewport.hwnd()).pixels == rotatedHome.pixels,
                 "Help button restores overlay without changing model view");
            SendMessageW(viewport.hwnd(), WM_MOUSEWHEEL, MAKEWPARAM(0, 120), 0);
            SendMessageW(viewport.hwnd(), WM_MBUTTONDBLCLK, MK_MBUTTON, MAKELPARAM(200, 200));
            until = GetTickCount64() + 5000;
            while (GetTickCount64() < until) {
                pump();
                if (SendMessageW(viewport.hwnd(), WM_APP + 74, 0, 0) ==
                    SendMessageW(viewport.hwnd(), WM_APP + 75, 0, 0))
                    break;
                Sleep(2);
            }
            test(captureWindow(viewport.hwnd()).pixels == rotatedHome.pixels,
                 "Middle double click preserves the rotated view direction");
            HDC dc = CreateCompatibleDC(nullptr);
            cad::Image capture{800, 600, {}};
            capture.pixels.assign(800 * 600, 0xff00ff00);
            auto bm = cad::bitmap(capture);
            auto old = SelectObject(dc, bm);
            SendMessageW(viewport.hwnd(), WM_PRINTCLIENT, reinterpret_cast<WPARAM>(dc), PRF_CLIENT);
            SelectObject(dc, old);
            BITMAPINFO bi{};
            bi.bmiHeader = {sizeof(BITMAPINFOHEADER), 800, -600, 1, 32, BI_RGB};
            GetDIBits(dc, bm, 0, 600, capture.pixels.data(), &bi, DIB_RGB_COLORS);
            DeleteObject(bm);
            DeleteDC(dc);
            size_t stale = 0, modelPixels = 0;
            for (auto pixel : capture.pixels) {
                stale += (pixel & 0xffffff) == 0x00ff00;
                auto red = (pixel >> 16) & 255, green = (pixel >> 8) & 255, blue = pixel & 255;
                modelPixels += red > green + 15 && green > blue + 15;
            }
            test(stale == 0, "Presentation covers the entire target with a completed composition");
            test(modelPixels > 100, "Presented drag frame contains the model");
            save(capture, cad::moduleDirectory() / "motion-validation.bmp");
            handles = GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS);
            for (int i = 0; i < 15; ++i) {
                HDC target = CreateCompatibleDC(nullptr);
                auto imageBitmap = cad::bitmap(capture);
                auto selected = SelectObject(target, imageBitmap);
                SendMessageW(viewport.hwnd(), WM_PRINTCLIENT, reinterpret_cast<WPARAM>(target),
                             PRF_CLIENT);
                SelectObject(target, selected);
                DeleteObject(imageBitmap);
                DeleteDC(target);
            }
            test(GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS) == handles,
                 "Repeated double-buffered presentation does not leak GDI objects");
        }
        {
            cad::Viewport localized(motionHost);
            RECT area{0, 0, 800, 600};
            localized.resize(area);
            if (!cad::loadSettings().help)
                localized.command(13);
            localized.open(root / "assembly.step");
            auto until = GetTickCount64() + 10000;
            while (GetTickCount64() < until &&
                   SendMessageW(localized.hwnd(), WM_APP + 72, 0, 0) == 0) {
                MSG event;
                while (PeekMessageW(&event, nullptr, 0, 0, PM_REMOVE)) {
                    TranslateMessage(&event);
                    DispatchMessageW(&event);
                }
                Sleep(2);
            }
            test(SendMessageW(localized.hwnd(), WM_APP + 72, 0, 0) == 1,
                 "English preview completes");
            auto visible = captureWindow(localized.hwnd());
            save(visible, cad::moduleDirectory() / L"english-help-validation.bmp");
            localized.command(13);
            test(captureWindow(localized.hwnd()).pixels != visible.pixels,
                 "English help can still be hidden");
            localized.command(13);
            test(captureWindow(localized.hwnd()).pixels == visible.pixels,
                 "English help restores without changing model view");
            auto menu = localized.createViewMenu();
            test(GetMenuState(menu, 9, MF_BYCOMMAND) != UINT(-1) &&
                     GetMenuState(menu, 10, MF_BYCOMMAND) != UINT(-1),
                 "3D menu retains edges and wireframe");
            DestroyMenu(menu);
            localized.open(root / "geometry.dxf");
            until = GetTickCount64() + 10000;
            while (GetTickCount64() < until &&
                   SendMessageW(localized.hwnd(), WM_APP + 72, 0, 0) == 0) {
                MSG event;
                while (PeekMessageW(&event, nullptr, 0, 0, PM_REMOVE))
                    DispatchMessageW(&event);
                Sleep(2);
            }
            test(SendMessageW(localized.hwnd(), WM_APP + 72, 0, 0) == 1,
                 "DXF menu test loads drawing");
            menu = localized.createViewMenu();
            for (unsigned id : {5, 6, 7, 8, 9, 10, 11, 14, 15})
                test(GetMenuState(menu, id, MF_BYCOMMAND) == UINT(-1),
                     "DXF menu omits 3D-only actions");
            for (unsigned id : {2, 3, 4, 12, 13, 1000})
                test(GetMenuState(menu, id, MF_BYCOMMAND) != UINT(-1),
                     "DXF menu retains useful actions and layers");
            wchar_t label[128]{};
            GetMenuStringW(menu, 12, label, 128, MF_BYCOMMAND);
            test(std::wstring(label) == L"File information", "Menu uses a single English label");
            DestroyMenu(menu);
            localized.command(12);
            auto text = GetDlgItem(localized.hwnd(), 201);
            std::wstring details(GetWindowTextLengthW(text) + 1, L'\0');
            GetWindowTextW(text, details.data(), int(details.size()));
            test(details.find(L"geometry.dxf") != std::wstring::npos,
                 "Information shows original filename");
            auto blankInfo = captureWindow(localized.hwnd());
            save(blankInfo, cad::moduleDirectory() / "file-information-validation.bmp");
            localized.command(12);
            test(!GetDlgItem(localized.hwnd(), 201),
                 "File information command toggles the panel closed");
        }
        DestroyWindow(motionHost);
        std::cout << checks << " native checks passed (direct COM host, not Explorer acceptance)\n";
        CoUninitialize();
        return 0;
    } catch (const std::exception &e) {
        std::cerr << "FAIL after " << checks << " native checks: " << e.what() << "\n";
        CoUninitialize();
        return 1;
    }
}
