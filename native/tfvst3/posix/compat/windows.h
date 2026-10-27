/* Stand-in for <windows.h> when tfvst3.cpp is built on macOS or Linux (the Windows build never sees this folder).
   tfvst3.cpp needs exactly three things from it: the HWND type, the CP_UTF8 / WideCharToMultiByte conversion of the plug-in path,
   and __declspec(dllexport) in tfvst3.h. Nothing else of Windows is used by the shim. */
#pragma once

#if defined(_WIN32)
#error "posix/compat/windows.h is for macOS and Linux only; Windows builds use Source/native/tfvst3/CMakeLists.txt."
#endif

#include <cstddef>
#include <cstdint>
#include <cwchar>

/* The editor handle the shim stores for a plug-in view. The editor itself is not hosted on these systems yet
   (a plug-in reports no HWND support, so tfv3_has_editor answers 0), but the member must exist. */
typedef void* HWND;

#define CP_UTF8 65001u

/* tfvst3.h marks every export __declspec(dllexport); here that means default symbol visibility (the library is built with hidden visibility otherwise). */
#define __declspec(attribute) __attribute__ ((visibility ("default")))

/* UTF-32 (wchar_t on macOS and Linux) to UTF-8, with the Windows call's contract for the one use in tfvst3.cpp:
   source length -1 = NUL terminated and counted in the result; a null destination or a zero size only measures. */
inline int WideCharToMultiByte (unsigned int, unsigned long, const wchar_t* source, int sourceLength, char* destination,
                                int destinationSize, const char*, int*)
{
	if (!source)
		return 0;
	std::size_t length = 0;
	if (sourceLength < 0)
		length = std::wcslen (source) + 1;
	else
		length = static_cast<std::size_t> (sourceLength);

	std::size_t written = 0;
	auto put = [&] (unsigned char byte) {
		if (destination && destinationSize > 0 && written < static_cast<std::size_t> (destinationSize))
			destination[written] = static_cast<char> (byte);
		++written;
	};
	for (std::size_t i = 0; i < length; ++i)
	{
		std::uint32_t c = static_cast<std::uint32_t> (source[i]);
		if (c > 0x10FFFFu || (c >= 0xD800u && c <= 0xDFFFu))
			c = 0xFFFDu; /* not a scalar value: the replacement character */
		if (c < 0x80u)
			put (static_cast<unsigned char> (c));
		else if (c < 0x800u)
		{
			put (static_cast<unsigned char> (0xC0u | (c >> 6)));
			put (static_cast<unsigned char> (0x80u | (c & 0x3Fu)));
		}
		else if (c < 0x10000u)
		{
			put (static_cast<unsigned char> (0xE0u | (c >> 12)));
			put (static_cast<unsigned char> (0x80u | ((c >> 6) & 0x3Fu)));
			put (static_cast<unsigned char> (0x80u | (c & 0x3Fu)));
		}
		else
		{
			put (static_cast<unsigned char> (0xF0u | (c >> 18)));
			put (static_cast<unsigned char> (0x80u | ((c >> 12) & 0x3Fu)));
			put (static_cast<unsigned char> (0x80u | ((c >> 6) & 0x3Fu)));
			put (static_cast<unsigned char> (0x80u | (c & 0x3Fu)));
		}
	}
	return static_cast<int> (written);
}
