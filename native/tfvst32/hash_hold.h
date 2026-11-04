// SHA-256 check over an open file handle and the hold-the-file load rule (BCrypt, no third-party code).
#pragma once

#include <string>

#include <windows.h>

namespace tfvst32 {

enum class HoldFailure { None, Open, Hash, Mismatch };

// Opens the DLL denying writers, hashes it when expectedSha256 is not empty/"none", and keeps the handle open.
// The caller must also use the returned handle and close it after the module has been mapped (see Vst2Host).
HANDLE HoldAndVerify(const std::wstring& path, const std::string& expectedSha256, HoldFailure& failure, std::string& error);

// Lower-case hex SHA-256 of the open file from its beginning; the file position is not restored.
bool Sha256File(HANDLE file, std::string& hex, std::string& error);

}  // namespace tfvst32
