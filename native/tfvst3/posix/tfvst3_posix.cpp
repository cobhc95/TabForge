// tfvst3 (macOS / Linux) - the two exports whose signature carries a text, in the form the Windows library has: the plug-in path as UTF-16.
//
// tfvst3.h declares them with `const wchar_t*`. On Windows that is UTF-16, which is also what the C# side (DllImport, CharSet.Unicode) hands over on
// every system. Here wchar_t is 32 bits, so tfvst3.cpp is built with those two functions renamed (see CMakeLists.txt: *_native) and these
// exports take the UTF-16 text, widen it and call them. Every other export of tfvst3.h is used unchanged from tfvst3.cpp.

#include <cstddef>
#include <cstdint>
#include <exception>
#include <string>

struct tfv3_plugin;

extern "C" {
int32_t tfv3_list_classes_native (const wchar_t* path, char (*names)[128], int32_t* is_instrument, int32_t max);
tfv3_plugin* tfv3_create_native (const wchar_t* path, int32_t class_index, double sample_rate, int32_t max_block, char* error_out);
}

#define TFV3_EXPORT extern "C" __attribute__ ((visibility ("default")))

namespace {

// UTF-16 to the platform's wchar_t (UTF-32); an unpaired surrogate becomes U+FFFD.
std::wstring widen (const char16_t* text)
{
	std::wstring result;
	if (!text)
		return result;
	for (; *text; ++text)
	{
		char32_t c = *text;
		if (c >= 0xD800 && c <= 0xDBFF && text[1] >= 0xDC00 && text[1] <= 0xDFFF)
		{
			c = 0x10000 + ((c - 0xD800) << 10) + (static_cast<char32_t> (text[1]) - 0xDC00);
			++text;
		}
		else if (c >= 0xD800 && c <= 0xDFFF)
			c = 0xFFFD;
		result.push_back (static_cast<wchar_t> (c));
	}
	return result;
}

void fail (char* error_out, const char* message)
{
	if (!error_out)
		return;
	std::size_t n = 0;
	while (message[n] && n < 511)
	{
		error_out[n] = message[n];
		++n;
	}
	error_out[n] = '\0';
}

} // namespace

/* Same contract as tfv3_list_classes in tfvst3.h; `path` is UTF-16. */
TFV3_EXPORT int32_t tfv3_list_classes (const char16_t* path, char (*names)[128], int32_t* is_instrument, int32_t max)
{
	try
	{
		return tfv3_list_classes_native (widen (path).c_str (), names, is_instrument, max);
	}
	catch (...)
	{
		return 0;
	}
}

/* Same contract as tfv3_create in tfvst3.h; `path` is UTF-16, error_out is 512 bytes UTF-8. */
TFV3_EXPORT tfv3_plugin* tfv3_create (const char16_t* path, int32_t class_index, double sample_rate, int32_t max_block, char* error_out)
{
	try
	{
		return tfv3_create_native (widen (path).c_str (), class_index, sample_rate, max_block, error_out);
	}
	catch (const std::exception&)
	{
		fail (error_out, "The VST3 plug-in could not be started.");
		return nullptr;
	}
	catch (...)
	{
		fail (error_out, "The VST3 plug-in could not be started.");
		return nullptr;
	}
}
