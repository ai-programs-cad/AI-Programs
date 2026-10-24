#include "cad/windows.hpp"
#include <fstream>
#include <shlwapi.h>
#include <shobjidl.h>
#include <thumbcache.h>
namespace {
// Keep these stable across upgrades. Installer and COM tests use the same IDs.
const CLSID thumbnail = {
    0x70d346a5, 0x7204, 0x45ee, {0xa8, 0x27, 0x75, 0x01, 0xb8, 0x44, 0x94, 0xe1}};
const CLSID preview = {
    0x62564304, 0x3ea6, 0x4b9e, {0x97, 0x0e, 0x45, 0x2a, 0x8a, 0xcb, 0xc5, 0xb3}};
std::atomic<long> objects{0};
class Handler final : public IInitializeWithFile,
                      public IInitializeWithStream,
                      public IThumbnailProvider,
                      public IPreviewHandler,
                      public IObjectWithSite {
    std::atomic<ULONG> refs_{1};
    std::filesystem::path file_, staged_;
    std::wstring displayName_;
    IStream *stream_ = nullptr;
    IUnknown *site_ = nullptr;
    HWND parent_ = nullptr;
    RECT rect_{};
    std::unique_ptr<cad::Viewport> view_;
    void stage() {
        if (!file_.empty())
            return;
        if (!stream_)
            throw cad::Error("No input file");
        STATSTG stat{};
        if (FAILED(stream_->Stat(&stat, STATFLAG_NONAME)) ||
            stat.cbSize.QuadPart > 256ull * 1024 * 1024)
            throw cad::Error("Stream exceeds file limit");
        std::wstring namedExtension;
        STATSTG named{};
        if (SUCCEEDED(stream_->Stat(&named, STATFLAG_DEFAULT))) {
            if (named.pwcsName) {
                namedExtension = std::filesystem::path(named.pwcsName).extension().wstring();
                displayName_ = std::filesystem::path(named.pwcsName).filename().wstring();
                CoTaskMemFree(named.pwcsName);
                for (auto &c : namedExtension)
                    c = wchar_t(towlower(c));
            }
        }
        LARGE_INTEGER zero{};
        if (FAILED(stream_->Seek(zero, STREAM_SEEK_SET, nullptr)))
            throw cad::Error("Cannot seek CAD stream");
        char header[65536]{};
        ULONG got = 0;
        if (FAILED(stream_->Read(header, sizeof(header), &got)))
            throw cad::Error("Cannot identify CAD stream");
        std::string h(header, got);
        std::wstring ext = h.find("ISO-10303-21") != std::string::npos ? L".step"
                           : h.find("SECTION") != std::string::npos ||
                                   h.find("AutoCAD Binary DXF") != std::string::npos
                               ? L".dxf"
                               : L".stl";
        if (namedExtension == L".dxf" || namedExtension == L".stl" || namedExtension == L".step" ||
            namedExtension == L".stp")
            ext = namedExtension;
        if (FAILED(stream_->Seek(zero, STREAM_SEEK_SET, nullptr)))
            throw cad::Error("Cannot rewind CAD stream");
        wchar_t g[40];
        GUID id;
        if (FAILED(CoCreateGuid(&id)))
            throw cad::Error("Cannot create stream identity");
        StringFromGUID2(id, g, 40);
        staged_ = cad::temporaryRoot() / (std::wstring(L"CADPreviewStream-") + g + ext);
        std::ofstream f(staged_, std::ios::binary);
        char buf[65536];
        uint64_t bytes = 0;
        for (;;) {
            ULONG n = 0;
            HRESULT hr = stream_->Read(buf, sizeof(buf), &n);
            if (FAILED(hr))
                throw cad::Error("Cannot read CAD stream");
            if (!n)
                break;
            bytes += n;
            if (bytes > 256ull * 1024 * 1024)
                throw cad::Error("Stream exceeds file limit");
            f.write(buf, n);
        }
        if (!f)
            throw cad::Error("Cannot stage CAD stream");
        f.close();
        file_ = staged_;
        if (displayName_.empty())
            displayName_ = L"CAD file (Explorer stream)";
    }

  public:
    Handler() { ++objects; }
    ~Handler() {
        Unload();
        if (site_)
            site_->Release();
        --objects;
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void **out) override {
        if (!out)
            return E_POINTER;
        *out = nullptr;
        if (id == IID_IUnknown || id == __uuidof(IInitializeWithFile))
            *out = static_cast<IInitializeWithFile *>(this);
        else if (id == __uuidof(IInitializeWithStream))
            *out = static_cast<IInitializeWithStream *>(this);
        else if (id == __uuidof(IThumbnailProvider))
            *out = static_cast<IThumbnailProvider *>(this);
        else if (id == __uuidof(IPreviewHandler))
            *out = static_cast<IPreviewHandler *>(this);
        else if (id == __uuidof(IObjectWithSite))
            *out = static_cast<IObjectWithSite *>(this);
        else
            return E_NOINTERFACE;
        AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG n = --refs_;
        if (!n)
            delete this;
        return n;
    }
    HRESULT STDMETHODCALLTYPE Initialize(LPCWSTR path, DWORD) override {
        if (!path)
            return E_POINTER;
        if (!file_.empty() || stream_)
            return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        try {
            file_ = path;
            return S_OK;
        } catch (...) {
            return E_FAIL;
        }
    }
    HRESULT STDMETHODCALLTYPE Initialize(IStream *stream, DWORD) override {
        if (!stream)
            return E_POINTER;
        if (!file_.empty() || stream_)
            return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        stream_ = stream;
        stream_->AddRef();
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetThumbnail(UINT cx, HBITMAP *out, WTS_ALPHATYPE *alpha) override {
        if (!out || !alpha)
            return E_POINTER;
        *out = nullptr;
        *alpha = WTSAT_UNKNOWN;
        if (cx == 0 || cx > 1024)
            return E_INVALIDARG;
        try {
            stage();
            cad::Image image;
            cad::importIsolated(file_, 10000, nullptr, &image, cx);
            *out = cad::bitmap(image);
            *alpha = WTSAT_RGB;
            return S_OK;
        } catch (...) {
            return E_FAIL;
        }
    }
    HRESULT STDMETHODCALLTYPE SetWindow(HWND parent, const RECT *r) override {
        if (!r)
            return E_POINTER;
        parent_ = parent;
        rect_ = *r;
        if (view_) {
            SetParent(view_->hwnd(), parent_);
            view_->resize(rect_);
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetRect(const RECT *r) override {
        if (!r)
            return E_POINTER;
        rect_ = *r;
        if (view_)
            view_->resize(rect_);
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE DoPreview() override {
        if (!parent_)
            return E_UNEXPECTED;
        if (view_)
            return S_OK;
        try {
            view_ = std::make_unique<cad::Viewport>(parent_);
            view_->resize(rect_);
            stage();
            view_->open(file_, displayName_);
            return S_OK;
        } catch (const std::exception &e) {
            if (view_) {
                view_->showError(e.what());
                return S_OK;
            }
            return E_FAIL;
        } catch (...) {
            view_.reset();
            return E_FAIL;
        }
    }
    HRESULT STDMETHODCALLTYPE Unload() override {
        // Stop the import before releasing its input. Explorer can reuse this COM object
        // for the next selection, so Unload must clear initialization as well as the HWND.
        view_.reset();
        if (stream_) {
            stream_->Release();
            stream_ = nullptr;
        }
        std::error_code ec;
        if (!staged_.empty())
            std::filesystem::remove(staged_, ec);
        staged_.clear();
        file_.clear();
        displayName_.clear();
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetFocus() override {
        if (view_)
            ::SetFocus(view_->hwnd());
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE QueryFocus(HWND *out) override {
        if (!out)
            return E_POINTER;
        *out = GetFocus();
        return *out ? S_OK : S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE TranslateAccelerator(MSG *msg) override {
        if (!msg)
            return E_POINTER;
        if (site_) {
            IPreviewHandlerFrame *frame = nullptr;
            if (SUCCEEDED(site_->QueryInterface(IID_PPV_ARGS(&frame)))) {
                auto hr = frame->TranslateAccelerator(msg);
                frame->Release();
                return hr;
            }
        }
        return S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown *site) override {
        if (site)
            site->AddRef();
        if (site_)
            site_->Release();
        site_ = site;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID id, void **out) override {
        if (!out)
            return E_POINTER;
        *out = nullptr;
        return site_ ? site_->QueryInterface(id, out) : E_FAIL;
    }
};
class Factory final : public IClassFactory {
    std::atomic<ULONG> refs_{1};

  public:
    Factory() { ++objects; }
    ~Factory() { --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void **out) override {
        if (!out)
            return E_POINTER;
        *out = nullptr;
        if (id != IID_IUnknown && id != IID_IClassFactory)
            return E_NOINTERFACE;
        *out = static_cast<IClassFactory *>(this);
        AddRef();
        return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
    ULONG STDMETHODCALLTYPE Release() override {
        auto n = --refs_;
        if (!n)
            delete this;
        return n;
    }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown *outer, REFIID id, void **out) override {
        if (!out)
            return E_POINTER;
        *out = nullptr;
        if (outer)
            return CLASS_E_NOAGGREGATION;
        try {
            auto h = new Handler;
            auto hr = h->QueryInterface(id, out);
            h->Release();
            return hr;
        } catch (...) {
            return E_OUTOFMEMORY;
        }
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override {
        objects += lock ? 1 : -1;
        return S_OK;
    }
};
} // namespace
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID cls, REFIID id, void **out) {
    if (!out)
        return E_POINTER;
    *out = nullptr;
    if (cls != thumbnail && cls != preview)
        return CLASS_E_CLASSNOTAVAILABLE;
    auto f = new (std::nothrow) Factory;
    if (!f)
        return E_OUTOFMEMORY;
    auto hr = f->QueryInterface(id, out);
    f->Release();
    return hr;
}
extern "C" HRESULT __stdcall DllCanUnloadNow() {
    return objects.load() == 0 ? S_OK : S_FALSE;
}
