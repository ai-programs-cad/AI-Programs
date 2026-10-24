#include "cad/windows.hpp"
#include <chrono>
#include <fstream>
#include <shlobj.h>
namespace cad {
std::filesystem::path temporaryRoot() {
    wchar_t overridePath[32768]{};
    DWORD n = GetEnvironmentVariableW(L"CADPREVIEW_TEMP_DIRECTORY", overridePath, 32768);
    if (n && n < 32768) {
        auto dir = std::filesystem::path(overridePath);
        if (!dir.is_absolute())
            throw Error("Temporary directory override must be absolute");
        std::filesystem::create_directories(dir);
        return dir;
    }
    PWSTR path = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppDataLow, KF_FLAG_DEFAULT, nullptr, &path)))
        throw Error("Cannot locate low-integrity temporary folder");
    auto dir = std::filesystem::path(path) / L"CADPreview" / L"Temp";
    CoTaskMemFree(path);
    std::filesystem::create_directories(dir);
    return dir;
}
std::filesystem::path moduleDirectory() {
    HMODULE module = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCWSTR>(&moduleDirectory), &module);
    std::wstring buf(32768, L'\0');
    auto n = GetModuleFileNameW(module, buf.data(), DWORD(buf.size()));
    if (!n || n == buf.size())
        throw Error("Cannot locate CAD Preview binaries");
    buf.resize(n);
    return std::filesystem::path(buf).parent_path();
}
namespace {
struct Handle {
    HANDLE h = nullptr;
    ~Handle() {
        if (h && h != INVALID_HANDLE_VALUE)
            CloseHandle(h);
    }
    operator HANDLE() const { return h; }
};
std::wstring quote(const std::wstring &s) {
    if (s.find(L'"') != std::wstring::npos)
        throw Error("Invalid path");
    return L"\"" + s + L"\"";
}
} // namespace
Scene importIsolated(const std::filesystem::path &source, unsigned timeoutMs,
                     const std::atomic_bool *cancel, Image *thumbnail, unsigned thumbnailSize) {
    if (cancel && cancel->load())
        throw Error("Cancelled");
    GUID id;
    if (FAILED(CoCreateGuid(&id)))
        throw Error("Cannot create worker identity");
    wchar_t guid[40];
    StringFromGUID2(id, guid, 40);
    auto dir = temporaryRoot() / (std::wstring(L"CADPreview-") + guid);
    std::filesystem::create_directory(dir);
    struct Cleanup {
        std::filesystem::path p;
        ~Cleanup() {
            std::error_code ec;
            std::filesystem::remove_all(p, ec);
        }
    } cleanup{dir};
    Limits limits;
    std::error_code ec;
    if (std::filesystem::file_size(source, ec) > limits.fileBytes || ec)
        throw Error("File is inaccessible or exceeds 256 MiB");
    auto input = dir / (L"input" + source.extension().wstring());
    {
        std::ifstream from(source, std::ios::binary);
        std::ofstream to(input, std::ios::binary);
        char buffer[65536];
        uint64_t total = 0;
        while (from) {
            from.read(buffer, sizeof(buffer));
            auto n = from.gcount();
            total += uint64_t(n);
            if (total > limits.fileBytes)
                throw Error("Source grew beyond size limit");
            if (cancel && cancel->load())
                throw Error("Cancelled");
            to.write(buffer, n);
        }
        if (!from.eof() || !to)
            throw Error("Cannot create read-only source snapshot");
    }
    auto output = dir / L"scene.bin";
    auto exe = moduleDirectory() / L"CADPreview.Worker.exe";
    Handle job{CreateJobObjectW(nullptr, nullptr)};
    if (!job.h)
        throw Error("Cannot create worker job");
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION li{};
    li.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE |
                                          JOB_OBJECT_LIMIT_PROCESS_MEMORY |
                                          JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
    li.ProcessMemoryLimit = 1024ull * 1024 * 1024;
    li.BasicLimitInformation.ActiveProcessLimit = 1;
    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &li, sizeof(li)))
        throw Error("Cannot apply worker limits");
    std::wstring cmd =
        quote(exe.wstring()) + L" " + quote(input.wstring()) + L" " + quote(output.wstring());
    STARTUPINFOW si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};
    if (thumbnail)
        cmd += L" " + std::to_wstring(thumbnailSize);
    if (!CreateProcessW(exe.c_str(), cmd.data(), nullptr, nullptr, FALSE,
                        CREATE_SUSPENDED | CREATE_NO_WINDOW, nullptr, moduleDirectory().c_str(),
                        &si, &pi))
        throw Error("Cannot launch CADPreview.Worker.exe (Windows error " +
                    std::to_string(GetLastError()) + ")");
    Handle process{pi.hProcess}, thread{pi.hThread};
    if (!AssignProcessToJobObject(job, process)) {
        const auto error = GetLastError();
        TerminateProcess(process, 4);
        WaitForSingleObject(process, 5000);
        throw Error("Cannot isolate CAD worker (Windows error " + std::to_string(error) + ")");
    }
    ResumeThread(thread);
    auto start = GetTickCount64();
    while (WaitForSingleObject(process, 25) == WAIT_TIMEOUT) {
        if ((cancel && cancel->load()) || GetTickCount64() - start > timeoutMs) {
            TerminateJobObject(job, 5);
            WaitForSingleObject(process, 5000);
            throw Error(cancel && cancel->load() ? "Cancelled" : "Import exceeded time limit");
        }
    }
    DWORD code = 0;
    GetExitCodeProcess(process, &code);
    if (code) {
        std::ifstream f(std::filesystem::path(output).concat(L".error"));
        std::string message;
        std::getline(f, message);
        throw Error(message.empty() ? "CAD import failed or memory limit exceeded" : message);
    }
    if (thumbnail) {
        if (!thumbnailSize || thumbnailSize > 1024 ||
            std::filesystem::file_size(output) != uint64_t(thumbnailSize) * thumbnailSize * 4)
            throw Error("Invalid thumbnail worker output");
        thumbnail->width = thumbnail->height = int(thumbnailSize);
        thumbnail->pixels.resize(size_t(thumbnailSize) * thumbnailSize);
        std::ifstream f(output, std::ios::binary);
        if (!f.read(reinterpret_cast<char *>(thumbnail->pixels.data()),
                    std::streamsize(thumbnail->pixels.size() * 4)))
            throw Error("Truncated thumbnail");
        return {};
    }
    return readScene(output, limits);
}
} // namespace cad
