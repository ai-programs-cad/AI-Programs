#pragma once
#include "core.hpp"
#include <atomic>
#include <condition_variable>
#include <memory>
#include <mutex>
#include <objbase.h>
#include <optional>
#include <thread>
#include <windows.h>
namespace cad {
struct Camera {
    double yaw = -.7853981634, pitch = .9553166181, zoom = 1, panX = 0, panY = 0;
    bool dark = true, edges = true, wire = false, help = true, contrast = true, gradient = true;
};
Camera loadSettings();
void saveSettings(const Camera &);
struct Image {
    int width = 0, height = 0;
    std::vector<uint32_t> pixels;
};
struct RenderControl {
    const std::atomic<uint64_t> *revision = nullptr;
    uint64_t expectedRevision = 0;
    const std::vector<bool> *visibleLayers = nullptr;
    unsigned timeoutMs = 10000;
};
Image render(const Scene &, int, int, const Camera &, const RenderControl & = {});
HBITMAP bitmap(const Image &);
std::wstring wide(const std::string &);
std::filesystem::path moduleDirectory();
std::filesystem::path temporaryRoot();
Scene importIsolated(const std::filesystem::path &, unsigned timeoutMs = 30000,
                     const std::atomic_bool *cancel = nullptr, Image *thumbnail = nullptr,
                     unsigned thumbnailSize = 0);
class Viewport {
    HWND window_ = nullptr;
    std::thread loader_;
    std::atomic_bool cancel_{false};
    std::mutex mutex_;
    std::shared_ptr<Scene> pending_;
    std::string pendingError_;
    std::shared_ptr<Scene> scene_;
    std::vector<bool> visibleLayers_;
    struct FrameRequest {
        std::shared_ptr<const Scene> scene;
        Camera camera;
        std::vector<bool> visibleLayers;
        int width = 0, height = 0;
        uint64_t revision = 0;
        uint64_t epoch = 0;
    };
    std::thread painter_;
    std::atomic<uint64_t> renderRevision_{0};
    std::atomic<uint64_t> sceneEpoch_{0};
    uint64_t pendingFrameEpoch_ = 0;
    ULONGLONG interactiveUntil_ = 0;
    HBITMAP backBuffer_ = nullptr;
    SIZE backBufferSize_{};
    std::mutex frameMutex_;
    std::condition_variable frameWake_;
    bool closing_ = false;
    std::optional<FrameRequest> requestedFrame_;
    std::shared_ptr<Image> frame_, pendingFrame_;
    std::string renderStatus_, pendingFrameError_;
    uint64_t pendingFrameRevision_ = 0, displayedFrameRevision_ = 0;
    std::string status_;
    Camera camera_;
    POINT last_{};
    bool moving_ = false;
    RECT helpButton_{};
    HWND infoText_ = nullptr, infoTitle_ = nullptr, infoClose_ = nullptr;
    std::wstring sourceName_;
    std::optional<uint64_t> sourceBytes_;
    void closeInformation();
    void layoutInformation();
    void toggleInformation();
    static LRESULT CALLBACK proc(HWND, UINT, WPARAM, LPARAM);
    LRESULT message(UINT, WPARAM, LPARAM);
    void menu(POINT);
    void paint();
    void paintTo(HDC, const RECT &);
    void requestFrame();
    void frameLoop(HWND);

  public:
    explicit Viewport(HWND parent);
    ~Viewport();
    HWND hwnd() const { return window_; }
    void open(const std::filesystem::path &, const std::wstring &displayName = {});
    void showError(const std::string &);
    void fit();
    void command(unsigned);
    void resize(const RECT &);
    void showMenu();
    HMENU createViewMenu(); // Caller owns the returned menu.
};
} // namespace cad
