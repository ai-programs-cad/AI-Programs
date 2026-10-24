#include "cad/windows.hpp"
#include <commdlg.h>
#include <shellapi.h>
namespace {
std::unique_ptr<cad::Viewport> view;
void openFile(HWND h) {
    wchar_t name[32768]{};
    OPENFILENAMEW of{};
    of.lStructSize = sizeof(of);
    of.hwndOwner = h;
    of.lpstrFilter = L"CAD files\0*.dxf;*.stl;*.step;*.stp\0All files\0*.*\0";
    of.lpstrFile = name;
    of.nMaxFile = 32768;
    of.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
    if (GetOpenFileNameW(&of))
        view->open(name);
}
LRESULT CALLBACK proc(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
    case WM_CREATE: {
        view = std::make_unique<cad::Viewport>(h);
        HMENU menu = CreateMenu();
        AppendMenuW(menu, MF_STRING, 1, L"Open file");
        AppendMenuW(menu, MF_STRING, 2, L"Fit to window");
        AppendMenuW(menu, MF_STRING, 20, L"View");
        AppendMenuW(menu, MF_STRING, 12, L"File information");
        SetMenu(h, menu);
        DragAcceptFiles(h, TRUE);
        return 0;
    }
    case WM_COMMAND:
        if (LOWORD(w) == 1)
            openFile(h);
        else if (LOWORD(w) == 20)
            view->showMenu();
        else
            view->command(LOWORD(w));
        return 0;
    case WM_DROPFILES: {
        HDROP drop = reinterpret_cast<HDROP>(w);
        wchar_t path[32768];
        if (DragQueryFileW(drop, 0, path, 32768))
            view->open(path);
        DragFinish(drop);
        return 0;
    }
    case WM_SIZE:
        if (view) {
            RECT r;
            GetClientRect(h, &r);
            view->resize(r);
        }
        return 0;
    case WM_DESTROY:
        view.reset();
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(h, m, w, l);
}
} // namespace
int WINAPI wWinMain(HINSTANCE inst, HINSTANCE, PWSTR, int show) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    WNDCLASSW wc{};
    wc.hInstance = inst;
    wc.lpfnWndProc = proc;
    wc.lpszClassName = L"CADPreviewViewer03";
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    RegisterClassW(&wc);
    HWND h = CreateWindowExW(0, wc.lpszClassName, L"CAD Preview for Windows — 0.4.0",
                             WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT, 1000, 760, nullptr,
                             nullptr, inst, nullptr);
    if (!h)
        return 1;
    ShowWindow(h, show);
    int argc;
    auto argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (argc > 1)
        view->open(argv[1]);
    LocalFree(argv);
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    CoUninitialize();
    return 0;
}
