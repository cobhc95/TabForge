// tfvst3 (Linux) - the process-wide registry behind every plug-in's RunLoop (see tfv3_runloop_linux.h) and tfv3_run_loop_wait, which runs it.
//
// Re-entrancy: a handler may register or unregister handlers (its own included) while it is being called, so the wait works on a copy of the
// entries and checks, just before each call, that the entry is still registered (by its serial number). The lock is never held during a call.
// Failure: a descriptor poll() calls invalid is dropped at once; one that reports an error or hang-up without data gets its handler called once
// more and is then dropped (otherwise poll() would return at once on every turn and spin the main thread). A handler that throws is ignored.

#include "tfv3_runloop_linux.h"

#include <poll.h>

#include <algorithm>
#include <chrono>
#include <mutex>
#include <vector>

using namespace Steinberg;
using namespace Steinberg::Linux;

namespace {

struct FdEntry
{
	uint64_t serial;
	tfv3::RunLoop* owner;
	IEventHandler* handler;
	int fd;
};

struct TimerEntry
{
	uint64_t serial;
	tfv3::RunLoop* owner;
	ITimerHandler* handler;
	int64_t intervalMs;
	int64_t dueMs;
};

struct Registry
{
	std::mutex lock;
	std::vector<FdEntry> fds;
	std::vector<TimerEntry> timers;
	uint64_t nextSerial = 1;
};

// Never destroyed: a RunLoop that outlives static destruction (an instance not freed before exit) must still find it.
Registry& registry ()
{
	static Registry* instance = new Registry ();
	return *instance;
}

constexpr int64_t kMinTimerMs = 1;
constexpr int64_t kMaxTimerMs = 24LL * 60 * 60 * 1000;
constexpr int32_t kMaxWaitMs = 1000;

int64_t nowMs ()
{
	using namespace std::chrono;
	return duration_cast<milliseconds> (steady_clock::now ().time_since_epoch ()).count ();
}

template <typename Entry>
bool registered (const std::vector<Entry>& entries, uint64_t serial)
{
	return std::any_of (entries.begin (), entries.end (), [serial] (const Entry& e) { return e.serial == serial; });
}

template <typename Entry>
void drop (std::vector<Entry>& entries, uint64_t serial)
{
	entries.erase (std::remove_if (entries.begin (), entries.end (), [serial] (const Entry& e) { return e.serial == serial; }), entries.end ());
}

} // namespace

namespace tfv3 {

tresult PLUGIN_API RunLoop::registerEventHandler (IEventHandler* handler, FileDescriptor fd)
{
	if (!handler || fd < 0)
		return kInvalidArgument;
	auto& r = registry ();
	std::lock_guard<std::mutex> guard (r.lock);
	for (const auto& e : r.fds)
		if (e.handler == handler && e.fd == fd)
			return kResultTrue; // already registered
	r.fds.push_back ({r.nextSerial++, this, handler, fd});
	return kResultTrue;
}

tresult PLUGIN_API RunLoop::unregisterEventHandler (IEventHandler* handler)
{
	if (!handler)
		return kInvalidArgument;
	auto& r = registry ();
	std::lock_guard<std::mutex> guard (r.lock);
	const auto before = r.fds.size ();
	r.fds.erase (std::remove_if (r.fds.begin (), r.fds.end (), [handler] (const FdEntry& e) { return e.handler == handler; }), r.fds.end ());
	return r.fds.size () != before ? kResultTrue : kInvalidArgument;
}

tresult PLUGIN_API RunLoop::registerTimer (ITimerHandler* handler, TimerInterval milliseconds)
{
	if (!handler)
		return kInvalidArgument;
	const int64_t interval = std::clamp (static_cast<int64_t> (std::min<TimerInterval> (milliseconds, static_cast<TimerInterval> (kMaxTimerMs))), kMinTimerMs, kMaxTimerMs);
	auto& r = registry ();
	std::lock_guard<std::mutex> guard (r.lock);
	r.timers.push_back ({r.nextSerial++, this, handler, interval, nowMs () + interval});
	return kResultTrue;
}

tresult PLUGIN_API RunLoop::unregisterTimer (ITimerHandler* handler)
{
	if (!handler)
		return kInvalidArgument;
	auto& r = registry ();
	std::lock_guard<std::mutex> guard (r.lock);
	const auto before = r.timers.size ();
	r.timers.erase (std::remove_if (r.timers.begin (), r.timers.end (), [handler] (const TimerEntry& e) { return e.handler == handler; }), r.timers.end ());
	return r.timers.size () != before ? kResultTrue : kInvalidArgument;
}

void RunLoop::clear ()
{
	auto& r = registry ();
	std::lock_guard<std::mutex> guard (r.lock);
	r.fds.erase (std::remove_if (r.fds.begin (), r.fds.end (), [this] (const FdEntry& e) { return e.owner == this; }), r.fds.end ());
	r.timers.erase (std::remove_if (r.timers.begin (), r.timers.end (), [this] (const TimerEntry& e) { return e.owner == this; }), r.timers.end ());
}

} // namespace tfv3

extern "C" __attribute__ ((visibility ("default"))) int32_t tfv3_run_loop_wait (int32_t extra_fd, int32_t wait_ms)
{
	try
	{
		auto& r = registry ();
		std::vector<FdEntry> watched;
		int64_t timeout = std::clamp<int64_t> (wait_ms, 0, kMaxWaitMs);
		{
			std::lock_guard<std::mutex> guard (r.lock);
			watched = r.fds;
			const int64_t now = nowMs ();
			for (const auto& t : r.timers)
				timeout = std::min (timeout, std::max<int64_t> (0, t.dueMs - now));
		}

		std::vector<pollfd> polled;
		polled.reserve (watched.size () + 1);
		for (const auto& e : watched)
			polled.push_back ({e.fd, POLLIN, 0});
		if (extra_fd >= 0)
			polled.push_back ({extra_fd, POLLIN, 0});
		const int ready = poll (polled.empty () ? nullptr : polled.data (), static_cast<nfds_t> (polled.size ()), static_cast<int> (timeout));

		int32_t extraReady = 0;
		if (ready > 0)
		{
			if (extra_fd >= 0 && (polled.back ().revents & (POLLIN | POLLHUP | POLLERR)))
				extraReady = 1;
			for (size_t i = 0; i < watched.size (); ++i)
			{
				const short events = polled[i].revents;
				if (events == 0)
					continue;
				const auto& e = watched[i];
				{
					std::lock_guard<std::mutex> guard (r.lock);
					if (!registered (r.fds, e.serial))
						continue;
					if (events & POLLNVAL)
					{
						drop (r.fds, e.serial);
						continue;
					}
				}
				try
				{
					e.handler->onFDIsSet (e.fd);
				}
				catch (...)
				{
				}
				if ((events & (POLLERR | POLLHUP)) && !(events & POLLIN))
				{
					std::lock_guard<std::mutex> guard (r.lock);
					drop (r.fds, e.serial);
				}
			}
		}

		// Timers due now, each called at most once; the next due time counts from now (no burst of catch-up calls after a stall).
		std::vector<TimerEntry> due;
		{
			std::lock_guard<std::mutex> guard (r.lock);
			const int64_t now = nowMs ();
			for (const auto& t : r.timers)
				if (t.dueMs <= now)
					due.push_back (t);
		}
		for (const auto& t : due)
		{
			{
				std::lock_guard<std::mutex> guard (r.lock);
				auto it = std::find_if (r.timers.begin (), r.timers.end (), [&t] (const TimerEntry& e) { return e.serial == t.serial; });
				if (it == r.timers.end ())
					continue;
				it->dueMs = nowMs () + it->intervalMs;
			}
			try
			{
				t.handler->onTimer ();
			}
			catch (...)
			{
			}
		}
		return extraReady;
	}
	catch (...)
	{
		return 0;
	}
}
