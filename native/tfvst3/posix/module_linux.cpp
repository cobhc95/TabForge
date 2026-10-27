// tfvst3 (Linux) - the platform half of the VST3 SDK's hosting Module: loads a .vst3 bundle with dlopen and calls the plug-in's ModuleEntry /
// ModuleExit around its GetPluginFactory (VST3 module format, "Contents/<arch>-linux/<name>.so").
// Replaces public.sdk/source/vst/hosting/module_linux.cpp in this build: only what the shim uses (Module::create), no module scanning,
// no snapshots. The declarations of the other Module statics stay unreferenced here and are therefore not defined.

#include "public.sdk/source/vst/hosting/module.h"

#include "pluginterfaces/base/funknownimpl.h"

#include <dlfcn.h>
#include <sys/stat.h>
#include <sys/utsname.h>

#include <memory>
#include <string>

extern "C" {
using ModuleEntryFunc = bool (PLUGIN_API*) (void*);
using ModuleExitFunc = bool (PLUGIN_API*) ();
}

namespace VST3 {
namespace Hosting {
namespace {

bool isDirectory (const std::string& path)
{
	struct stat info {};
	return stat (path.c_str (), &info) == 0 && S_ISDIR (info.st_mode);
}

// The machine name of "uname -m" (x86_64, aarch64) is the folder prefix of the bundle layout: Contents/x86_64-linux.
std::string machineName ()
{
	struct utsname data {};
	if (uname (&data) != 0)
		return {};
	return data.machine;
}

// "/x/y/Foo.vst3" -> "Foo"
std::string stemOf (const std::string& path)
{
	auto end = path.size ();
	while (end > 0 && path[end - 1] == '/')
		--end;
	auto begin = path.find_last_of ('/', end == 0 ? 0 : end - 1);
	begin = begin == std::string::npos ? 0 : begin + 1;
	auto name = path.substr (begin, end - begin);
	auto dot = name.find_last_of ('.');
	return dot == std::string::npos || dot == 0 ? name : name.substr (0, dot);
}

class LinuxModule final : public Module
{
public:
	~LinuxModule () override
	{
		factory = PluginFactory (nullptr);
		if (handle)
		{
			if (auto moduleExit = symbol<ModuleExitFunc> ("ModuleExit"))
				moduleExit ();
			dlclose (handle);
		}
	}

	bool load (const std::string& bundlePath, std::string& errorDescription) override
	{
		if (!isDirectory (bundlePath))
		{
			errorDescription = bundlePath + " is not a module directory.";
			return false;
		}
		auto machine = machineName ();
		if (machine.empty ())
		{
			errorDescription = "Could not get the current machine name.";
			return false;
		}
		auto binary = bundlePath;
		if (binary.back () != '/')
			binary += '/';
		binary += "Contents/" + machine + "-linux/" + stemOf (bundlePath) + ".so";

		handle = dlopen (binary.c_str (), RTLD_LAZY);
		if (!handle)
		{
			const char* why = dlerror ();
			errorDescription = std::string ("dlopen failed.\n") + (why ? why : "");
			return false;
		}
		// ModuleEntry, ModuleExit and GetPluginFactory are all mandatory.
		auto moduleEntry = symbol<ModuleEntryFunc> ("ModuleEntry");
		if (!moduleEntry)
		{
			errorDescription = "The shared library does not export the required 'ModuleEntry' function";
			return false;
		}
		if (!symbol<ModuleExitFunc> ("ModuleExit"))
		{
			errorDescription = "The shared library does not export the required 'ModuleExit' function";
			return false;
		}
		auto factoryProc = symbol<GetFactoryProc> ("GetPluginFactory");
		if (!factoryProc)
		{
			errorDescription = "The shared library does not export the required 'GetPluginFactory' function";
			return false;
		}
		if (!moduleEntry (handle))
		{
			errorDescription = "Calling 'ModuleEntry' failed";
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
		return reinterpret_cast<T> (dlsym (handle, name));
	}

	void* handle {nullptr};
};

} // anonymous

Module::Ptr Module::create (const std::string& path, std::string& errorDescription)
{
	auto created = std::make_shared<LinuxModule> ();
	if (!created->load (path, errorDescription))
		return nullptr;
	created->path = path;
	auto slash = path.find_last_of ('/');
	created->name = slash == std::string::npos ? path : path.substr (slash + 1);
	return created;
}

} // Hosting
} // VST3
