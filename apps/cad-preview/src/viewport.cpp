#include "cad/help_text.hpp"
#include "cad/windows.hpp"
#include <algorithm>
#include <iomanip>
#include <sstream>
#include <windowsx.h>
namespace cad {
namespace {
constexpr UINT ready = WM_APP + 71;
constexpr UINT frameReady = WM_APP + 73;
std::atomic<unsigned> viewportCount{0};
std::mutex viewportClassMutex;
} // namespace
Viewport::Viewport(HWND parent) {
    camera_ = loadSettings();
    std::unique_lock<std::mutex> classLock(viewportClassMutex);
    WNDCLASSW wc{};
    wc.lpfnWndProc = proc;
    wc.style = CS_DBLCLKS;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = L"CADPreviewViewport03";
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    RegisterClassW(&wc);
    window_ = CreateWindowExW(0, wc.lpszClassName, L"CAD Preview",
                              WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN, 0, 0, 100,
                              100, parent, nullptr, wc.hInstance, this);
    if (!window_) {
        if (viewportCount == 0)
            UnregisterClassW(wc.lpszClassName, wc.hInstance);
        throw Error("Cannot create preview window");
    }
    ++viewportCount;
    classLock.unlock();
    try {
        painter_ = std::thread([this, target = window_] { frameLoop(target); });
    } catch (...) {
        std::lock_guard<std::mutex> lock(viewportClassMutex);
        DestroyWindow(window_);
        if (--viewportCount == 0)
            UnregisterClassW(wc.lpszClassName, wc.hInstance);
        throw;
    }
}
Viewport::~Viewport() {
    saveSettings(camera_);
    cancel_ = true;
    ++sceneEpoch_;
    ++renderRevision_;
    {
        std::lock_guard<std::mutex> lock(frameMutex_);
        closing_ = true;
        requestedFrame_.reset();
    }
    frameWake_.notify_one();
    if (loader_.joinable())
        loader_.join();
    if (painter_.joinable())
        painter_.join();
    std::lock_guard<std::mutex> classLock(viewportClassMutex);
    if (window_)
        DestroyWindow(window_);
    if (backBuffer_)
        DeleteObject(backBuffer_);
    if (--viewportCount == 0)
        UnregisterClassW(L"CADPreviewViewport03", GetModuleHandleW(nullptr));
}
void Viewport::resize(const RECT &r) {
    MoveWindow(window_, r.left, r.top, r.right - r.left, r.bottom - r.top, TRUE);
}
void Viewport::open(const std::filesystem::path &p, const std::wstring &displayName) {
    closeInformation();
    sourceName_ = displayName.empty() ? p.filename().wstring() : displayName;
    std::error_code sizeError;
    auto bytes = std::filesystem::file_size(p, sizeError);
    sourceBytes_ = sizeError ? std::optional<uint64_t>{} : bytes;
    ++sceneEpoch_;
    ++renderRevision_;
    {
        std::lock_guard<std::mutex> lock(frameMutex_);
        requestedFrame_.reset();
        pendingFrame_.reset();
    }
    frame_.reset();
    visibleLayers_.clear();
    renderStatus_.clear();
    cancel_ = true;
    if (loader_.joinable())
        loader_.join();
    MSG stale;
    while (PeekMessageW(&stale, window_, ready, ready, PM_REMOVE)) {
    }
    {
        std::lock_guard<std::mutex> guard(mutex_);
        pending_.reset();
        pendingError_.clear();
    }
    cancel_ = false;
    scene_.reset();
    status_ = "Loading...";
    fit();
    loader_ = std::thread([this, p, target = window_] {
        try {
            auto s = std::make_shared<Scene>(importIsolated(p, 30000, &cancel_));
            std::lock_guard<std::mutex> guard(mutex_);
            pending_ = std::move(s);
            pendingError_.clear();
        } catch (const std::exception &e) {
            std::lock_guard<std::mutex> guard(mutex_);
            pending_.reset();
            pendingError_ = e.what();
        }
        if (!cancel_)
            PostMessageW(target, ready, 0, 0);
    });
    InvalidateRect(window_, nullptr, FALSE);
}
void Viewport::showError(const std::string &error) {
    ++sceneEpoch_;
    cancel_ = true;
    if (loader_.joinable())
        loader_.join();
    ++renderRevision_;
    scene_.reset();
    frame_.reset();
    renderStatus_.clear();
    status_ = "Cannot preview this file / Не удалось открыть файл\n\n" + error;
    InvalidateRect(window_, nullptr, FALSE);
}
void Viewport::fit() {
    if (scene_)
        ++sceneEpoch_;
    camera_.zoom = 1;
    camera_.panX = camera_.panY = 0;
    requestFrame();
}
void Viewport::command(unsigned id) {
    if (scene_ && scene_->flat && id >= 5 && id <= 11)
        return;
    if (id == 12) {
        toggleInformation();
        return;
    }
    if (id == 16) {
        closeInformation();
        return;
    }
    if (id == 13) {
        camera_.help = !camera_.help;
        saveSettings(camera_);
        InvalidateRect(window_, nullptr, FALSE);
        return;
    }
    if (id != 12)
        ++sceneEpoch_;
    switch (id) {
    case 2:
        fit();
        break;
    case 3:
        camera_.zoom = std::min(1000., camera_.zoom * 1.2);
        break;
    case 4:
        camera_.zoom = std::max(.001, camera_.zoom / 1.2);
        break;
    case 5:
        camera_.yaw = -.7853981634;
        camera_.pitch = .9553166181;
        fit();
        break;
    case 6:
        camera_.yaw = 0;
        camera_.pitch = 1.57079632679;
        fit();
        break;
    case 7:
        camera_.yaw = 0;
        camera_.pitch = 0;
        fit();
        break;
    case 8:
        camera_.yaw = -1.57079632679;
        camera_.pitch = 1.57079632679;
        fit();
        break;
    case 9:
        camera_.edges = !camera_.edges;
        break;
    case 10:
        camera_.wire = !camera_.wire;
        break;
    case 11:
        camera_.dark = !camera_.dark;
        break;
    case 14:
        camera_.contrast = !camera_.contrast;
        break;
    case 15:
        camera_.gradient = !camera_.gradient;
        break;
    default:
        if (id >= 1000 && id - 1000 < visibleLayers_.size())
            visibleLayers_[id - 1000] = !visibleLayers_[id - 1000];
    }
    requestFrame();
}
HMENU Viewport::createViewMenu() {
    HMENU m = CreatePopupMenu();
    const wchar_t *labels[] = {L"Fit to window",   L"Zoom in",
                               L"Zoom out",        L"Isometric",
                               L"Front",           L"Top",
                               L"Right",           L"Show edges",
                               L"Wireframe",       L"Dark solid background",
                               L"File information"};
    for (unsigned j = 0; j < 11; ++j) {
        if (scene_ && scene_->flat && j >= 3 && j <= 9)
            continue;
        UINT flags = MF_STRING;
        if ((j == 7 && camera_.edges) || (j == 8 && camera_.wire) || (j == 9 && camera_.dark))
            flags |= MF_CHECKED;
        AppendMenuW(m, flags, j + 2, labels[j]);
    }
    AppendMenuW(m, MF_STRING | (camera_.help ? MF_CHECKED : 0), 13, helpText().menu);
    if (scene_ && !scene_->flat)
        AppendMenuW(m, MF_STRING | (camera_.contrast ? MF_CHECKED : 0), 14,
                    L"Contrast model color");
    if (scene_ && !scene_->flat)
        AppendMenuW(m, MF_STRING | (camera_.gradient ? MF_CHECKED : 0), 15,
                    L"Light gradient background");
    if (scene_ && scene_->flat) {
        HMENU layers = CreatePopupMenu();
        for (size_t j = 0; j < scene_->layers.size() && j < 1000; ++j) {
            auto name = wide(scene_->layers[j].name);
            AppendMenuW(layers, MF_STRING | (visibleLayers_[j] ? MF_CHECKED : 0), 1000 + j,
                        name.c_str());
        }
        AppendMenuW(m, MF_POPUP, reinterpret_cast<UINT_PTR>(layers), L"Layers");
    }
    return m;
}
void Viewport::menu(POINT p) {
    HMENU m = createViewMenu();
    unsigned id = TrackPopupMenu(m, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.x, p.y, 0, window_, nullptr);
    DestroyMenu(m);
    if (id)
        command(id);
}
void Viewport::showMenu() {
    POINT point{};
    GetCursorPos(&point);
    menu(point);
}
void Viewport::closeInformation() {
    for (auto control : {infoText_, infoTitle_, infoClose_})
        if (control)
            DestroyWindow(control);
    infoText_ = infoTitle_ = infoClose_ = nullptr;
    if (window_)
        InvalidateRect(window_, nullptr, FALSE);
}
void Viewport::layoutInformation() {
    if (!infoText_)
        return;
    RECT area{};
    GetClientRect(window_, &area);
    auto px = [this](int value) { return MulDiv(value, GetDpiForWindow(window_), 96); };
    int pad = std::min<int>(px(12), std::min(area.right, area.bottom) / 8);
    const int maxWidth = std::max<int>(1, std::min<int>(px(480), area.right - 2 * pad));
    const int maxHeight = std::max<int>(1, area.bottom - 2 * pad);
    int header = std::min(px(28), maxHeight / 3);
    std::wstring text(GetWindowTextLengthW(infoText_) + 1, L'\0');
    text.resize(GetWindowTextW(infoText_, text.data(), int(text.size())));
    HDC dc = GetDC(infoText_);
    if (!dc)
        return;
    auto previous =
        SelectObject(dc, reinterpret_cast<HFONT>(SendMessageW(infoText_, WM_GETFONT, 0, 0)));
    RECT measured{};
    DrawTextW(dc, text.c_str(), -1, &measured, DT_CALCRECT | DT_NOPREFIX);
    TEXTMETRICW metrics{};
    GetTextMetricsW(dc, &metrics);
    SelectObject(dc, previous);
    ReleaseDC(infoText_, dc);
    const int textPad = px(8);
    int width =
        std::min<int>(maxWidth, std::max<int>(px(200), measured.right + 2 * textPad + px(4)));
    // Measure wrapped lines in the real edit control, using its font and current DPI.
    ShowScrollBar(infoText_, SB_VERT, FALSE);
    SetWindowPos(infoText_, nullptr, pad, pad + header, width, std::max(1, maxHeight - header),
                 SWP_NOZORDER | SWP_NOACTIVATE);
    SendMessageW(infoText_, EM_SETMARGINS, EC_LEFTMARGIN | EC_RIGHTMARGIN,
                 MAKELPARAM(textPad, textPad));
    auto contentHeight = [&] {
        return int(SendMessageW(infoText_, EM_GETLINECOUNT, 0, 0)) *
                   std::max<int>(1, metrics.tmHeight) +
               px(8);
    };
    int height = std::min(maxHeight, header + contentHeight());
    if (header + contentHeight() > maxHeight) {
        ShowScrollBar(infoText_, SB_VERT, TRUE);
        height = std::min(maxHeight, header + contentHeight());
    }
    int button = std::min(px(72), width / 3);
    SetWindowPos(infoTitle_, HWND_TOP, pad, pad, width - button, header, SWP_SHOWWINDOW);
    SetWindowPos(infoClose_, HWND_TOP, pad + width - button, pad, button, header, SWP_SHOWWINDOW);
    SetWindowPos(infoText_, HWND_TOP, pad, pad + header, width, std::max(1, height - header),
                 SWP_SHOWWINDOW);
}
void Viewport::toggleInformation() {
    if (infoText_) {
        closeInformation();
        return;
    }
    std::wostringstream info;
    info << L"Name: " << sourceName_ << L"\r\n";
    if (sourceBytes_)
        info << L"File size: " << *sourceBytes_ << L" bytes (" << std::fixed << std::setprecision(1)
             << double(*sourceBytes_) / 1024 << L" KiB)\r\n";
    if (scene_) {
        auto size = scene_->bounds.size();
        info << std::defaultfloat << std::setprecision(8) << L"Format: " << wide(scene_->format)
             << L"\r\n"
             << L"Dimensions: " << size.x << L" x " << size.y;
        if (!scene_->flat)
            info << L" x " << size.z;
        info << L"\r\nUnits: " << wide(scene_->units) << L"\r\n"
             << L"Layers: " << scene_->layers.size() << L"\r\n"
             << (scene_->flat ? L"Drawing paths: " : L"Edge paths: ") << scene_->paths.size()
             << L"\r\n";
        if (!scene_->flat)
            info << L"Triangles: " << scene_->triangles.size() << L"\r\n";
        info << L"Text entities: " << scene_->texts.size() << L"\r\n";
        for (const auto &warning : scene_->warnings)
            info << L"\r\nWarning: " << wide(warning) << L"\r\n";
    } else {
        info << L"\r\n" << wide(status_) << L"\r\n" << wide(renderStatus_);
    }
    auto instance = GetModuleHandleW(nullptr);
    infoTitle_ = CreateWindowExW(0, L"STATIC", L"File information", WS_CHILD | SS_CENTERIMAGE, 0, 0,
                                 1, 1, window_, nullptr, instance, nullptr);
    infoClose_ = CreateWindowExW(0, L"BUTTON", L"Close", WS_CHILD | WS_TABSTOP, 0, 0, 1, 1, window_,
                                 reinterpret_cast<HMENU>(16), instance, nullptr);
    auto contents = info.str();
    while (!contents.empty() && (contents.back() == L'\r' || contents.back() == L'\n'))
        contents.pop_back();
    infoText_ =
        CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", contents.c_str(),
                        WS_CHILD | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL, 0, 0,
                        1, 1, window_, reinterpret_cast<HMENU>(201), instance, nullptr);
    if (!infoText_ || !infoTitle_ || !infoClose_) {
        closeInformation();
        return;
    }
    for (auto control : {infoText_, infoTitle_, infoClose_})
        SendMessageW(control, WM_SETFONT,
                     reinterpret_cast<WPARAM>(GetStockObject(DEFAULT_GUI_FONT)), TRUE);
    layoutInformation();
}
void Viewport::paint() {
    PAINTSTRUCT ps;
    HDC dc = BeginPaint(window_, &ps);
    RECT r;
    GetClientRect(window_, &r);
    paintTo(dc, r);
    EndPaint(window_, &ps);
}
void Viewport::paintTo(HDC destination, const RECT &r) {
    const bool flat = scene_ && scene_->flat;
    const bool darkUi = flat || camera_.dark;
    if (r.right <= 0 || r.bottom <= 0)
        return;
    HDC dc = CreateCompatibleDC(destination);
    if (!dc)
        return;
    if (!backBuffer_ || backBufferSize_.cx != r.right || backBufferSize_.cy != r.bottom) {
        auto replacement = CreateCompatibleBitmap(destination, r.right, r.bottom);
        if (!replacement) {
            DeleteDC(dc);
            return;
        }
        if (backBuffer_)
            DeleteObject(backBuffer_);
        backBuffer_ = replacement;
        backBufferSize_ = {r.right, r.bottom};
    }
    auto previous = SelectObject(dc, backBuffer_);
    try {
        if (frame_ && r.right > 0 && r.bottom > 0) {
            const auto &im = *frame_;
            int w = im.width, h = im.height;
            const double ratio = std::min(double(r.right) / w, double(r.bottom) / h);
            const int displayWidth = std::max(1, int(w * ratio));
            const int displayHeight = std::max(1, int(h * ratio));
            const int left = (r.right - displayWidth) / 2, top = (r.bottom - displayHeight) / 2;
            auto background = CreateSolidBrush(flat           ? RGB(33, 40, 48)
                                               : camera_.dark ? RGB(32, 38, 49)
                                                              : RGB(244, 245, 247));
            FillRect(dc, &r, background);
            DeleteObject(background);
            BITMAPINFO bi{};
            bi.bmiHeader = {sizeof(BITMAPINFOHEADER), w, -h, 1, 32, BI_RGB};
            StretchDIBits(dc, left, top, displayWidth, displayHeight, 0, 0, w, h, im.pixels.data(),
                          &bi, DIB_RGB_COLORS, SRCCOPY);
        } else {
            auto brush = CreateSolidBrush(RGB(32, 38, 49));
            FillRect(dc, &r, brush);
            DeleteObject(brush);
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, RGB(230, 235, 245));
            auto text = wide(!renderStatus_.empty() ? renderStatus_
                             : status_.empty()      ? "Open a DXF, STL, STEP or STP file"
                                                    : status_);
            auto textRect = r;
            DrawTextW(dc, text.c_str(), -1, &textRect, DT_CENTER | DT_VCENTER | DT_WORDBREAK);
        }
        if (frame_ && !renderStatus_.empty()) {
            SetBkMode(dc, OPAQUE);
            SetBkColor(dc, RGB(32, 38, 49));
            SetTextColor(dc, RGB(240, 240, 245));
            auto text = wide(renderStatus_);
            RECT message{8, 8, r.right - 8, 64};
            DrawTextW(dc, text.c_str(), -1, &message, DT_LEFT | DT_WORDBREAK);
        }
    } catch (const std::exception &e) {
        status_ = e.what();
    }
    helpButton_ = {};
    if (frame_ && r.right >= 120 && r.bottom >= 100) {
        const auto &help = helpText();
        const int dpi = GetDpiForWindow(window_);
        const int unit = std::max(1, dpi);
        auto px = [unit](int value) { return MulDiv(value, unit, 96); };
        const int pad = px(12);
        const int header = px(36), footer = px(28);
        auto font = CreateFontW(-px(12), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                                OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                                DEFAULT_PITCH, L"Segoe UI");
        auto titleFont = CreateFontW(-px(13), 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
                                     DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                                     CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
        auto oldFont = SelectObject(dc, font);
        auto textWidth = [&](const wchar_t *text) {
            SIZE size{};
            GetTextExtentPoint32W(dc, text, lstrlenW(text), &size);
            return int(size.cx);
        };
        int actionWidth = 0, inputWidth = 0;
        for (int i = 0; i < 5; ++i) {
            actionWidth = std::max(actionWidth, textWidth(help.actions[i]));
            inputWidth = std::max(inputWidth, textWidth(help.inputs[i]));
        }
        const int needed = actionWidth + inputWidth + 3 * pad;
        const int panelWidth =
            std::min<int>(r.right - 2 * pad, std::clamp(needed, px(420), px(480)));
        const bool stacked = panelWidth < needed;
        const int rowHeight = px(stacked ? 42 : 28);
        const bool expanded = camera_.help && r.bottom >= header + footer + 5 * rowHeight + 3 * pad;
        const int panelHeight = expanded ? header + 5 * rowHeight + footer + pad : header;
        SelectObject(dc, titleFont);
        const int showWidth = textWidth(help.show) + 2 * pad;
        const int hideWidth = std::max(px(72), textWidth(help.hide) + 2 * pad);
        SelectObject(dc, font);
        const int width = expanded ? panelWidth : std::min(panelWidth, showWidth);
        RECT panel{pad, r.bottom - pad - panelHeight, pad + width, r.bottom - pad};
        const COLORREF surface = darkUi ? RGB(39, 47, 60) : RGB(255, 255, 255);
        const COLORREF border = darkUi ? RGB(65, 78, 96) : RGB(210, 218, 228);
        const COLORREF primary = darkUi ? RGB(233, 239, 247) : RGB(34, 46, 62);
        const COLORREF secondary = darkUi ? RGB(166, 182, 204) : RGB(91, 107, 128);
        const COLORREF accent = darkUi ? RGB(137, 194, 255) : RGB(32, 99, 171);
        auto brush = CreateSolidBrush(surface);
        auto pen = CreatePen(PS_SOLID, 1, border);
        auto oldBrush = SelectObject(dc, brush);
        auto oldPen = SelectObject(dc, pen);
        RoundRect(dc, panel.left, panel.top, panel.right, panel.bottom, px(12), px(12));
        SelectObject(dc, oldBrush);
        SelectObject(dc, oldPen);
        DeleteObject(brush);
        DeleteObject(pen);
        SetBkMode(dc, TRANSPARENT);
        auto draw = [&](const wchar_t *label, RECT bounds, COLORREF color, UINT flags = DT_LEFT) {
            SetTextColor(dc, color);
            DrawTextW(dc, label, -1, &bounds, flags | DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS);
        };
        helpButton_ = {panel.left, panel.top, panel.right, panel.top + header};
        SelectObject(dc, titleFont);
        RECT title{panel.left + pad, panel.top, panel.right - pad, panel.top + header};
        if (expanded)
            title.right -= hideWidth + pad;
        draw(expanded ? help.title : help.show, title, primary);
        SelectObject(dc, font);
        if (expanded) {
            RECT hide{panel.right - pad - hideWidth, panel.top + px(7), panel.right - pad,
                      panel.top + header - px(7)};
            auto buttonBrush = CreateSolidBrush(darkUi ? RGB(54, 67, 84) : RGB(235, 241, 248));
            FillRect(dc, &hide, buttonBrush);
            DeleteObject(buttonBrush);
            draw(help.hide, hide, accent, DT_CENTER);
            const int split = panel.left + actionWidth + 2 * pad;
            for (int i = 0; i < 5; ++i) {
                const int top = panel.top + header + i * rowHeight;
                RECT action{panel.left + pad, top, stacked ? panel.right - pad : split,
                            top + rowHeight};
                RECT input{stacked ? panel.left + pad : split, top, panel.right - pad,
                           top + rowHeight};
                if (stacked) {
                    action.bottom = top + px(20);
                    input.top = top + px(20);
                }
                draw(help.actions[i == 0 && flat ? 2 : i], action, secondary);
                draw(help.inputs[i], input, primary);
            }
            const int dividerY = panel.top + header + 5 * rowHeight + px(4);
            auto divider = CreatePen(PS_SOLID, 1, border);
            oldPen = SelectObject(dc, divider);
            MoveToEx(dc, panel.left + pad, dividerY, nullptr);
            LineTo(dc, panel.right - pad, dividerY);
            SelectObject(dc, oldPen);
            DeleteObject(divider);
            RECT note{panel.left + pad, dividerY + px(4), panel.right - pad, panel.bottom - px(4)};
            draw(help.note, note, secondary);
        }
        SelectObject(dc, oldFont);
        DeleteObject(font);
        DeleteObject(titleFont);
    }
    // Only the completed composition reaches the window; background clearing is offscreen.
    BitBlt(destination, 0, 0, r.right, r.bottom, dc, 0, 0, SRCCOPY);
    SelectObject(dc, previous);
    DeleteDC(dc);
}
void Viewport::requestFrame() {
    if (scene_ && window_) {
        RECT r{};
        GetClientRect(window_, &r);
        auto revision = ++renderRevision_;
        if (r.right > 0 && r.bottom > 0) {
            const bool interactive = moving_ || GetTickCount64() < interactiveUntil_;
            double ratio = std::min(1., (interactive ? 640. : 1600.) / std::max(r.right, r.bottom));
            FrameRequest request;
            request.scene = scene_;
            request.camera = camera_;
            request.camera.panX *= ratio;
            request.camera.panY *= ratio;
            request.width = std::max(1, int(r.right * ratio));
            request.height = std::max(1, int(r.bottom * ratio));
            request.visibleLayers = visibleLayers_;
            request.revision = revision;
            request.epoch = sceneEpoch_.load();
            {
                std::lock_guard<std::mutex> lock(frameMutex_);
                requestedFrame_ = std::move(request);
            }
            // Keep the last image quietly visible during interaction.
            renderStatus_ = frame_ ? "" : "Rendering... / Отрисовка...";
            frameWake_.notify_one();
        }
    }
    if (!frame_)
        InvalidateRect(window_, nullptr, FALSE);
}
void Viewport::frameLoop(HWND target) {
    for (;;) {
        FrameRequest request;
        {
            std::unique_lock<std::mutex> lock(frameMutex_);
            frameWake_.wait(lock, [this] { return closing_ || requestedFrame_.has_value(); });
            if (closing_)
                return;
            request = std::move(*requestedFrame_);
            requestedFrame_.reset();
        }
        std::shared_ptr<Image> image;
        std::string error;
        try {
            RenderControl control{&sceneEpoch_, request.epoch, &request.visibleLayers, 5000};
            image = std::make_shared<Image>(
                render(*request.scene, request.width, request.height, request.camera, control));
        } catch (const std::exception &e) {
            error = e.what();
        }
        {
            std::lock_guard<std::mutex> lock(frameMutex_);
            if (closing_ || sceneEpoch_.load() != request.epoch)
                continue;
            pendingFrame_ = std::move(image);
            pendingFrameError_ = std::move(error);
            pendingFrameRevision_ = request.revision;
            pendingFrameEpoch_ = request.epoch;
        }
        PostMessageW(target, frameReady, 0, 0);
    }
}
LRESULT CALLBACK Viewport::proc(HWND h, UINT m, WPARAM w, LPARAM l) {
    auto self = reinterpret_cast<Viewport *>(GetWindowLongPtrW(h, GWLP_USERDATA));
    if (m == WM_NCCREATE) {
        self = static_cast<Viewport *>(reinterpret_cast<CREATESTRUCTW *>(l)->lpCreateParams);
        self->window_ = h;
        SetWindowLongPtrW(h, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
    }
    if (self)
        return self->message(m, w, l);
    return DefWindowProcW(h, m, w, l);
}
LRESULT Viewport::message(UINT m, WPARAM w, LPARAM l) {
    switch (m) {
    case WM_APP + 72:
        return frame_ ? 1
               : status_ == "Loading..." ||
                       (scene_ && renderStatus_ == "Rendering... / Отрисовка...")
                   ? 0
                   : -1;
    case WM_APP + 74:
        return LRESULT(displayedFrameRevision_);
    case WM_APP + 75:
        return LRESULT(renderRevision_.load());
    case WM_APP + 77:
        return frame_ ? MAKELONG(frame_->width, frame_->height) : 0;
    case frameReady: {
        std::lock_guard<std::mutex> lock(frameMutex_);
        if (pendingFrameEpoch_ == sceneEpoch_.load() &&
            pendingFrameRevision_ > displayedFrameRevision_) {
            if (pendingFrame_)
                frame_ = std::move(pendingFrame_);
            renderStatus_ = pendingFrameError_;
            displayedFrameRevision_ = pendingFrameRevision_;
            InvalidateRect(window_, nullptr, FALSE);
        }
        return 0;
    }
    case ready: {
        std::lock_guard<std::mutex> guard(mutex_);
        scene_ = std::move(pending_);
        status_ = pendingError_;
        visibleLayers_.clear();
        if (scene_)
            for (const auto &layer : scene_->layers)
                visibleLayers_.push_back(layer.visible);
        fit();
        if (infoText_) {
            closeInformation();
            toggleInformation();
        }
        return 0;
    }
    case WM_PAINT:
        paint();
        return 0;
    case WM_PRINTCLIENT: {
        RECT r{};
        GetClientRect(window_, &r);
        paintTo(reinterpret_cast<HDC>(w), r);
        return 0;
    }
    case WM_ERASEBKGND:
        return 1;
    case WM_SIZE:
        layoutInformation();
        requestFrame();
        InvalidateRect(window_, nullptr, FALSE);
        return 0;
    case WM_COMMAND:
        if (LOWORD(w) >= 2 && LOWORD(w) <= 16 && HIWORD(w) == 0) {
            command(LOWORD(w));
            return 0;
        }
        break;
    case WM_LBUTTONDOWN:
    case WM_LBUTTONDBLCLK:
    case WM_MBUTTONDOWN:
        if (m != WM_MBUTTONDOWN &&
            PtInRect(&helpButton_, POINT{GET_X_LPARAM(l), GET_Y_LPARAM(l)})) {
            SetFocus(window_);
            command(13);
            return 0;
        }
        if (!moving_)
            ++sceneEpoch_;
        SetFocus(window_);
        SetCapture(window_);
        last_ = {GET_X_LPARAM(l), GET_Y_LPARAM(l)};
        moving_ = true;
        return 0;
    case WM_LBUTTONUP:
    case WM_MBUTTONUP:
        ReleaseCapture();
        moving_ = false;
        requestFrame();
        return 0;
    case WM_CAPTURECHANGED:
        moving_ = false;
        requestFrame();
        return 0;
    case WM_MBUTTONDBLCLK:
        moving_ = false;
        if (GetCapture() == window_)
            ReleaseCapture();
        interactiveUntil_ = 0;
        KillTimer(window_, 1);
        fit();
        return 0;
    case WM_MOUSEMOVE:
        if (moving_) {
            POINT p{GET_X_LPARAM(l), GET_Y_LPARAM(l)};
            double dx = p.x - last_.x, dy = p.y - last_.y;
            if ((w & MK_MBUTTON) || (w & MK_SHIFT) || (scene_ && scene_->flat)) {
                camera_.panX += dx;
                camera_.panY += dy;
            } else {
                camera_.yaw += dx * .01;
                camera_.pitch += dy * .01;
            }
            last_ = p;
            requestFrame();
        }
        return 0;
    case WM_MOUSEWHEEL:
        if (GetTickCount64() >= interactiveUntil_)
            ++sceneEpoch_;
        interactiveUntil_ = GetTickCount64() + 150;
        SetTimer(window_, 1, 150, nullptr);
        camera_.zoom = std::clamp(camera_.zoom * std::pow(1.15, GET_WHEEL_DELTA_WPARAM(w) / 120.),
                                  .001, 1000.);
        requestFrame();
        return 0;
    case WM_TIMER:
        if (w == 1 && GetTickCount64() >= interactiveUntil_) {
            KillTimer(window_, 1);
            requestFrame();
        }
        return 0;
    case WM_CONTEXTMENU: {
        POINT p{GET_X_LPARAM(l), GET_Y_LPARAM(l)};
        if (p.x == -1) {
            p = {10, 10};
            ClientToScreen(window_, &p);
        }
        menu(p);
        return 0;
    }
    case WM_KEYDOWN:
        if (w == 'F')
            fit();
        if (w == 'E')
            command(9);
        if (w == 'W')
            command(10);
        return 0;
    case WM_NCDESTROY:
        SetWindowLongPtrW(window_, GWLP_USERDATA, 0);
        window_ = nullptr;
        return 0;
    }
    return DefWindowProcW(window_, m, w, l);
}
} // namespace cad
