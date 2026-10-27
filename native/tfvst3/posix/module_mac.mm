// tfvst3 (macOS) - the platform half of the VST3 SDK's hosting Module: loads a .vst3 bundle with CFBundle and calls the plug-in's bundleEntry /
// bundleExit around its GetPluginFactory (VST3 module format, "Contents/MacOS/<name>").
// Replaces public.sdk/source/vst/hosting/module_mac.mm in this build: only what the shim uses (Module::create), no module scanning, no snapshots,
// and no Cocoa (CoreFoundation only). The declarations of the other Module statics stay unreferenced here and are therefore not defined.

#include "public.sdk/source/vst/hosting/module.h"

#include "pluginterfaces/base/funknownimpl.h"

#include <CoreFoundation/CoreFoundation.h>
#include <limits.h>
#include <unistd.h>

#include <memory>
#include <string>

extern "C" {
typedef bool (*BundleEntryFunc) (CFBundleRef);
typedef bool (*BundleExitFunc) ();
}

namespace VST3 {
namespace Hosting {
namespace {

// Owns one Core Foundation object and releases it.
template <typename T>
class CFHolder
{
public:
	explicit CFHolder (T object = nullptr) : object (object) {}
	CFHolder (const CFHolder&) = delete;
	CFHolder& operator= (const CFHolder&) = delete;
	~CFHolder ()
	{
		if (object)
			CFRelease (object);
	}
	T get () const { return object; }
	explicit operator bool () const { return object != nullptr; }

private:
	T object;
};

std::string describe (CFErrorRef error)
{
	CFHolder<CFStringRef> text (CFErrorCopyDescription (error));
	if (!text)
		return {};
	auto capacity = CFStringGetMaximumSizeForEncoding (CFStringGetLength (text.get ()), kCFStringEncodingUTF8) + 1;
	auto buffer = std::make_unique<char[]> (static_cast<size_t> (capacity));
	if (!CFStringGetCString (text.get (), buffer.get (), capacity, kCFStringEncodingUTF8))
		return {};
	return buffer.get ();
}

class MacModule final : public Module
{
public:
	~MacModule () override
	{
		factory = PluginFactory (nullptr);
		if (bundle)
		{
			if (auto bundleExit = symbol<BundleExitFunc> ("bundleExit"))
				bundleExit ();
			CFRelease (bundle);
		}
	}

	bool load (const std::string& givenPath, std::string& errorDescription) override
	{
		std::string bundlePath = givenPath;
		if (!bundlePath.empty () && bundlePath[0] != '/')
		{
			char directory[PATH_MAX];
			if (getcwd (directory, sizeof (directory)))
				bundlePath = std::string (directory) + "/" + givenPath;
		}
		CFHolder<CFURLRef> url (CFURLCreateFromFileSystemRepresentation (
		    kCFAllocatorDefault, reinterpret_cast<const UInt8*> (bundlePath.data ()), static_cast<CFIndex> (bundlePath.length ()), true));
		if (!url)
		{
			errorDescription = "Could not make a URL of the path: " + bundlePath;
			return false;
		}
		bundle = CFBundleCreate (kCFAllocatorDefault, url.get ());
		CFErrorRef error = nullptr;
		if (!bundle || !CFBundleLoadExecutableAndReturnError (bundle, &error))
		{
			if (error)
			{
				errorDescription = describe (error);
				CFRelease (error);
			}
			if (errorDescription.empty ())
				errorDescription = "Could not create Bundle for path: " + bundlePath;
			return false;
		}
		// bundleEntry, bundleExit and GetPluginFactory are all mandatory.
		auto bundleEntry = symbol<BundleEntryFunc> ("bundleEntry");
		if (!bundleEntry)
		{
			errorDescription = "Bundle does not export the required 'bundleEntry' function";
			return false;
		}
		if (!symbol<BundleExitFunc> ("bundleExit"))
		{
			errorDescription = "Bundle does not export the required 'bundleExit' function";
			return false;
		}
		auto factoryProc = symbol<GetFactoryProc> ("GetPluginFactory");
		if (!factoryProc)
		{
			errorDescription = "Bundle does not export the required 'GetPluginFactory' function";
			return false;
		}
		if (!bundleEntry (bundle))
		{
			errorDescription = "Calling 'bundleEntry' failed";
			return false;
		}
		auto f = Steinberg::U::cast<Steinberg::IPluginFactory> (Steinberg::owned (factoryProc ()));
		if (!f)
		{
			errorDescription = "Calling 'GetPluginFactory' returned nullptr";
			return false;
		}
		factory = PluginFactory (f);
		return true;
	}

private:
	template <typename T>
	T symbol (const char* name)
	{
		if (!bundle)
			return nullptr;
		CFHolder<CFStringRef> functionName (CFStringCreateWithCString (kCFAllocatorDefault, name, kCFStringEncodingASCII));
		return reinterpret_cast<T> (CFBundleGetFunctionPointerForName (bundle, functionName.get ()));
	}

	CFBundleRef bundle {nullptr};
};

} // anonymous

Module::Ptr Module::create (const std::string& path, std::string& errorDescription)
{
	auto created = std::make_shared<MacModule> ();
	if (!created->load (path, errorDescription))
		return nullptr;
	created->path = path;
	auto slash = path.find_last_of ('/');
	created->name = slash == std::string::npos ? path : path.substr (slash + 1);
	return created;
}

} // Hosting
} // VST3
