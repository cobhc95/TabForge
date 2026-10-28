// tfvst3 (Linux) - the host run loop a VST3 editor needs on Linux (Steinberg::Linux::IRunLoop): there is no system-wide event loop there, so the
// plug-in registers its file descriptors (usually its own X11 connection) and timers with the host, and the host calls them back.
//
// Each plug-in instance owns one RunLoop, handed out by its IPlugFrame (tfvst3.cpp). Every handler lands in one process-wide registry that
// tfv3_run_loop_wait runs: always on the engine's main thread (the thread that created the view), never on the audio thread.
// Handlers are held as plain pointers (no addRef / release): the plug-in unregisters them before freeing them, and RunLoop::clear drops whatever an
// instance left behind when its editor closes, so a closed editor is never called again.
#pragma once

#include "pluginterfaces/base/funknown.h"
#include "pluginterfaces/gui/iplugview.h"

#include <cstdint>

namespace tfv3 {

class RunLoop final : public Steinberg::Linux::IRunLoop
{
public:
	RunLoop () = default;
	RunLoop (const RunLoop&) = delete;
	RunLoop& operator= (const RunLoop&) = delete;
	~RunLoop () { clear (); }

	Steinberg::tresult PLUGIN_API registerEventHandler (Steinberg::Linux::IEventHandler* handler, Steinberg::Linux::FileDescriptor fd) override;
	Steinberg::tresult PLUGIN_API unregisterEventHandler (Steinberg::Linux::IEventHandler* handler) override;
	Steinberg::tresult PLUGIN_API registerTimer (Steinberg::Linux::ITimerHandler* handler, Steinberg::Linux::TimerInterval milliseconds) override;
	Steinberg::tresult PLUGIN_API unregisterTimer (Steinberg::Linux::ITimerHandler* handler) override;

	/* Drops every handler this instance registered (the editor closed); none of them is called again. */
	void clear ();

	Steinberg::tresult PLUGIN_API queryInterface (const Steinberg::TUID _iid, void** obj) override
	{
		if (Steinberg::FUnknownPrivate::iidEqual (_iid, Steinberg::Linux::IRunLoop::iid) ||
		    Steinberg::FUnknownPrivate::iidEqual (_iid, Steinberg::FUnknown::iid))
		{
			*obj = static_cast<Steinberg::Linux::IRunLoop*> (this);
			return Steinberg::kResultOk;
		}
		*obj = nullptr;
		return Steinberg::kNoInterface;
	}
	// Owned by the plug-in instance: the count is irrelevant.
	Steinberg::uint32 PLUGIN_API addRef () override { return 1; }
	Steinberg::uint32 PLUGIN_API release () override { return 1; }
};

} // namespace tfv3

/* Engine main thread: waits up to wait_ms (0..1000) for a registered file descriptor (or extra_fd, -1 = none) to become readable or the next
   timer to fall due, then calls the handlers that are ready (each timer at most once per call). Returns 1 when extra_fd is readable, else 0.
   Exported from libtfvst3.so only; the Windows tfvst3.dll has no such entry point. */
extern "C" __attribute__ ((visibility ("default"))) int32_t tfv3_run_loop_wait (int32_t extra_fd, int32_t wait_ms);
