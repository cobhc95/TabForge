#include "hash_hold.h"

#include <bcrypt.h>

#include <cctype>
#include <vector>

namespace tfvst32 {
namespace {
std::string Lower(std::string text) {
    for (char& c : text) c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return text;
}
}  // namespace

bool Sha256File(HANDLE file, std::string& hex, std::string& error) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD objectSize = 0, resultSize = 0, received = 0;
    std::vector<uint8_t> object;
    uint8_t digest[32];
    bool ok = false;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) {
        error = "SHA-256 is not available";
        return false;
    }
    if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectSize),
                          sizeof(objectSize), &received, 0) < 0 ||
        BCryptGetProperty(algorithm, BCRYPT_HASH_LENGTH, reinterpret_cast<PUCHAR>(&resultSize),
                          sizeof(resultSize), &received, 0) < 0 ||
        resultSize != sizeof(digest)) {
        BCryptCloseAlgorithmProvider(algorithm, 0);
        error = "SHA-256 could not be configured";
        return false;
    }
    object.resize(objectSize);
    if (BCryptCreateHash(algorithm, &hash, object.data(), objectSize, nullptr, 0, 0) < 0) {
        BCryptCloseAlgorithmProvider(algorithm, 0);
        error = "SHA-256 could not start";
        return false;
    }
    SetFilePointer(file, 0, nullptr, FILE_BEGIN);
    std::vector<uint8_t> buffer(64 * 1024);
    DWORD read = 0;
    while (true) {
        if (!ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr)) {
            error = "the plug-in file could not be read (error " + std::to_string(GetLastError()) + ")";
            break;
        }
        if (read == 0) { ok = true; break; }
        if (BCryptHashData(hash, buffer.data(), read, 0) < 0) {
            error = "SHA-256 failed while reading the plug-in file";
            break;
        }
    }
    if (ok && BCryptFinishHash(hash, digest, sizeof(digest), 0) < 0) {
        error = "SHA-256 could not finish";
        ok = false;
    }
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    if (!ok) return false;
    static const char* digits = "0123456789abcdef";
    hex.clear();
    hex.reserve(64);
    for (uint8_t byte : digest) {
        hex.push_back(digits[byte >> 4]);
        hex.push_back(digits[byte & 0x0F]);
    }
    return true;
}

HANDLE HoldAndVerify(const std::wstring& path, const std::string& expectedSha256, HoldFailure& failure, std::string& error) {
    failure = HoldFailure::None;
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
                              FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        failure = HoldFailure::Open;
        error = "the plug-in file could not be opened (error " + std::to_string(GetLastError()) + ")";
        return INVALID_HANDLE_VALUE;
    }
    if (!expectedSha256.empty() && expectedSha256 != "none") {
        std::string actual;
        if (!Sha256File(file, actual, error)) {
            failure = HoldFailure::Hash;
            CloseHandle(file);
            return INVALID_HANDLE_VALUE;
        }
        if (Lower(actual) != Lower(expectedSha256)) {
            failure = HoldFailure::Mismatch;
            error = "the plug-in file changed since it was approved (SHA-256 mismatch)";
            CloseHandle(file);
            return INVALID_HANDLE_VALUE;
        }
    }
    return file;
}

}  // namespace tfvst32
