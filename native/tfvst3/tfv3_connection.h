// tfv3_connection.h - the host-side pieces tfvst3.cpp uses to carry traffic from a plug-in's processor to its controller:
//   * TfMessage / TfAttributeList: the IMessage / IAttributeList plug-ins get from IHostApplication::createInstance (copyable, unlike the SDK ones);
//   * MessageHub + QueuedConnection: replace the SDK ConnectionProxy, which silently drops every message sent from a thread other than the one
//     that created it (so a processor message sent from its audio or worker thread never reached the controller). Messages from the thread that
//     drains the hub are delivered at once; messages from other threads are queued and delivered by tfv3_editor_idle on the editor (UI) thread;
//   * OutParamRing: processor -> controller parameter values (ProcessData::outputParameterChanges), audio thread to editor thread.
// Header only; no Windows API, so the macOS / Linux build compiles it unchanged.
#pragma once

#include "pluginterfaces/base/funknown.h"
#include "pluginterfaces/base/ipluginbase.h"
#include "pluginterfaces/vst/ivstmessage.h"
#include "pluginterfaces/vst/vsttypes.h"

#include <atomic>
#include <cstring>
#include <deque>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace tfv3 {

using namespace Steinberg;
using namespace Steinberg::Vst;

// Reference-counted FUnknown for the objects handed to plug-ins (they keep references, so the count is real).
#define TFV3_REFCOUNTED(Iface)                                                                        \
	std::atomic<uint32> refs {1};                                                                     \
	tresult PLUGIN_API queryInterface (const TUID _iid, void** obj) override                          \
	{                                                                                                 \
		if (FUnknownPrivate::iidEqual (_iid, Iface::iid) || FUnknownPrivate::iidEqual (_iid, FUnknown::iid)) \
		{                                                                                             \
			*obj = static_cast<Iface*> (this);                                                        \
			addRef ();                                                                                \
			return kResultOk;                                                                         \
		}                                                                                             \
		*obj = nullptr;                                                                               \
		return kNoInterface;                                                                          \
	}                                                                                                 \
	uint32 PLUGIN_API addRef () override { return ++refs; }                                           \
	uint32 PLUGIN_API release () override                                                             \
	{                                                                                                 \
		const uint32 n = --refs;                                                                      \
		if (n == 0)                                                                                   \
			delete this;                                                                              \
		return n;                                                                                     \
	}

//------------------------------------------------------------------------------------------------
// IAttributeList / IMessage
//------------------------------------------------------------------------------------------------
class TfAttributeList final : public IAttributeList
{
public:
	static TfAttributeList* make () { return new TfAttributeList (); } // refcount 1

	tresult PLUGIN_API setInt (AttrID aid, int64 value) override
	{
		Attr& a = put (aid, Kind::Int);
		a.i = value;
		return kResultTrue;
	}
	tresult PLUGIN_API getInt (AttrID aid, int64& value) override
	{
		const Attr* a = find (aid, Kind::Int);
		if (!a)
			return kResultFalse;
		value = a->i;
		return kResultTrue;
	}
	tresult PLUGIN_API setFloat (AttrID aid, double value) override
	{
		Attr& a = put (aid, Kind::Float);
		a.f = value;
		return kResultTrue;
	}
	tresult PLUGIN_API getFloat (AttrID aid, double& value) override
	{
		const Attr* a = find (aid, Kind::Float);
		if (!a)
			return kResultFalse;
		value = a->f;
		return kResultTrue;
	}
	tresult PLUGIN_API setString (AttrID aid, const TChar* string) override
	{
		if (!string)
			return kInvalidArgument;
		Attr& a = put (aid, Kind::String);
		a.s.clear ();
		for (const TChar* c = string; *c; ++c)
			a.s.push_back (*c);
		a.s.push_back (0);
		return kResultTrue;
	}
	tresult PLUGIN_API getString (AttrID aid, TChar* string, uint32 sizeInBytes) override
	{
		const Attr* a = find (aid, Kind::String);
		if (!a || !string || sizeInBytes < sizeof (TChar))
			return kResultFalse;
		const size_t room = sizeInBytes / sizeof (TChar);
		const size_t n = a->s.size () < room ? a->s.size () : room;
		std::memcpy (string, a->s.data (), n * sizeof (TChar));
		string[n - 1] = 0;
		return kResultTrue;
	}
	tresult PLUGIN_API setBinary (AttrID aid, const void* data, uint32 sizeInBytes) override
	{
		if (!data && sizeInBytes)
			return kInvalidArgument;
		Attr& a = put (aid, Kind::Binary);
		const char* bytes = static_cast<const char*> (data);
		a.b.assign (bytes, bytes + sizeInBytes);
		return kResultTrue;
	}
	tresult PLUGIN_API getBinary (AttrID aid, const void*& data, uint32& sizeInBytes) override
	{
		const Attr* a = find (aid, Kind::Binary);
		if (!a)
			return kResultFalse;
		data = a->b.data ();
		sizeInBytes = static_cast<uint32> (a->b.size ());
		return kResultTrue;
	}

	TfAttributeList* clone () const
	{
		auto* c = new TfAttributeList ();
		c->list = list;
		return c;
	}

	TFV3_REFCOUNTED (IAttributeList)

private:
	enum class Kind { Int, Float, String, Binary };
	struct Attr
	{
		Kind kind = Kind::Int;
		int64 i = 0;
		double f = 0;
		std::vector<TChar> s;
		std::vector<char> b;
	};
	std::map<std::string, Attr> list;

	TfAttributeList () = default;
	~TfAttributeList () = default;

	Attr& put (AttrID aid, Kind k)
	{
		Attr& a = list[aid ? aid : ""];
		a.kind = k;
		return a;
	}
	const Attr* find (AttrID aid, Kind k) const
	{
		auto it = list.find (aid ? aid : "");
		return (it != list.end () && it->second.kind == k) ? &it->second : nullptr;
	}
};

class TfMessage final : public IMessage
{
public:
	TfMessage () : attributes (TfAttributeList::make ()) {}
	FIDString PLUGIN_API getMessageID () override { return id.c_str (); }
	void PLUGIN_API setMessageID (FIDString messageID) override { id = messageID ? messageID : ""; }
	IAttributeList* PLUGIN_API getAttributes () override { return attributes; }

	/// An independent copy (refcount 1), so a plug-in may reuse or change the original after it sent it.
	TfMessage* clone () const
	{
		auto* c = new TfMessage ();
		c->id = id;
		c->attributes->release ();
		c->attributes = attributes->clone ();
		return c;
	}

	TFV3_REFCOUNTED (IMessage)

private:
	~TfMessage () { attributes->release (); }
	std::string id;
	TfAttributeList* attributes; // owned reference
};

//------------------------------------------------------------------------------------------------
// Message hub: queued delivery of IConnectionPoint::notify to the editor thread
//------------------------------------------------------------------------------------------------
inline thread_local int gInProcess = 0; // > 0 on a thread currently inside tfv3_process (the audio thread)

inline size_t currentThreadToken () { return std::hash<std::thread::id> () (std::this_thread::get_id ()); }

struct ProcessScope
{
	ProcessScope () { ++gInProcess; }
	~ProcessScope () { --gInProcess; }
};

class MessageHub
{
public:
	static constexpr uint32 kRing = 64;       // audio-thread messages per side (power of two)
	static constexpr size_t kMaxQueued = 512; // other-thread messages per side; the oldest are dropped beyond this

	/// Messages waiting for one side (the controller or the component).
	struct Port
	{
		IPtr<IConnectionPoint> target;
		std::mutex mutex;
		std::deque<IPtr<IMessage>> queue; // other threads
		IPtr<IMessage> ring[kRing];       // the audio thread (single producer) -> drain (single consumer)
		std::atomic<uint32> w {0}, r {0};
	};
	Port toController, toComponent;

	MessageHub () : mainToken (currentThreadToken ()) {}

	/// IConnectionPoint::notify of a proxy whose far side is port.target.
	tresult post (Port& port, IMessage* msg)
	{
		if (!msg || !port.target)
			return kResultFalse;
		if (gInProcess > 0)
		{
			// Audio thread: no allocation, no lock. The plug-in made this message itself, so keep a reference instead of copying.
			const uint32 w = port.w.load (std::memory_order_relaxed);
			if (w - port.r.load (std::memory_order_acquire) >= kRing)
				return kResultFalse;
			port.ring[w & (kRing - 1)] = msg;
			port.w.store (w + 1, std::memory_order_release);
			return kResultTrue;
		}
		if (mainToken.load (std::memory_order_relaxed) == currentThreadToken ())
			return port.target->notify (msg);
		IPtr<IMessage> keep = copyOf (msg);
		std::lock_guard<std::mutex> lock (port.mutex);
		if (port.queue.size () >= kMaxQueued)
			port.queue.pop_front ();
		port.queue.push_back (std::move (keep));
		return kResultTrue;
	}

	/// Editor thread: this thread becomes the delivery thread, and everything queued is delivered to its target.
	void drain ()
	{
		mainToken.store (currentThreadToken (), std::memory_order_relaxed);
		drainPort (toController);
		drainPort (toComponent);
	}

	/// Main thread after processing stopped and the connections were closed.
	void clear ()
	{
		for (Port* p : {&toController, &toComponent})
		{
			std::lock_guard<std::mutex> lock (p->mutex);
			p->queue.clear ();
			for (auto& m : p->ring)
				m = nullptr;
			p->w.store (0);
			p->r.store (0);
			p->target = nullptr;
		}
	}

	/// Number of messages waiting (tests).
	size_t pending (Port& port)
	{
		std::lock_guard<std::mutex> lock (port.mutex);
		return port.queue.size () + (port.w.load () - port.r.load ());
	}

private:
	std::atomic<size_t> mainToken;

	static IPtr<IMessage> copyOf (IMessage* msg)
	{
		if (auto* mine = dynamic_cast<TfMessage*> (msg))
			return IPtr<IMessage> (static_cast<IMessage*> (mine->clone ()), false);
		return IPtr<IMessage> (msg); // a plug-in own IMessage cannot be copied: keep a reference
	}

	static void drainPort (Port& port)
	{
		IPtr<IConnectionPoint> target = port.target;
		if (!target)
			return;
		uint32 r = port.r.load (std::memory_order_relaxed);
		const uint32 w = port.w.load (std::memory_order_acquire);
		for (; r != w; ++r)
		{
			IPtr<IMessage> m = std::move (port.ring[r & (kRing - 1)]);
			port.r.store (r + 1, std::memory_order_release);
			if (m)
				target->notify (m);
		}
		for (size_t n = 0; n < kMaxQueued; ++n)
		{
			IPtr<IMessage> m;
			{
				std::lock_guard<std::mutex> lock (port.mutex);
				if (port.queue.empty ())
					break;
				m = std::move (port.queue.front ());
				port.queue.pop_front ();
			}
			if (m)
				target->notify (m);
		}
	}
};

// Replaces the SDK ConnectionProxy: src is the IConnectionPoint of the side that sends through this proxy; what it notifies goes to the port target.
class QueuedConnection final : public IConnectionPoint
{
public:
	QueuedConnection (IConnectionPoint* src, MessageHub* hubIn, MessageHub::Port* portIn) : srcConnection (src), hub (hubIn), port (portIn) {}

	tresult PLUGIN_API connect (IConnectionPoint* other) override
	{
		if (!other)
			return kInvalidArgument;
		if (dstConnection)
			return kResultFalse;
		dstConnection = other;
		port->target = other;
		const tresult res = srcConnection->connect (this);
		if (res != kResultTrue)
		{
			dstConnection = nullptr;
			port->target = nullptr;
		}
		return res;
	}
	tresult PLUGIN_API disconnect (IConnectionPoint* other) override
	{
		if (!other)
			return kInvalidArgument;
		if (other == dstConnection)
		{
			if (srcConnection)
				srcConnection->disconnect (this);
			dstConnection = nullptr;
			return kResultTrue;
		}
		return kInvalidArgument;
	}
	tresult PLUGIN_API notify (IMessage* message) override { return hub->post (*port, message); }
	bool disconnect () { return disconnect (dstConnection) == kResultTrue; }

	TFV3_REFCOUNTED (IConnectionPoint)

private:
	~QueuedConnection () = default;
	IPtr<IConnectionPoint> srcConnection, dstConnection;
	MessageHub* hub;
	MessageHub::Port* port;
};

//------------------------------------------------------------------------------------------------
// Processor -> controller parameter values. One producer (audio thread, push), one consumer (editor thread, pop).
// A full ring overwrites the oldest entry (per-slot sequence stamps let the consumer notice and skip an entry that was overwritten).
//------------------------------------------------------------------------------------------------
class OutParamRing
{
public:
	static constexpr uint64 kSize = 512; // power of two

	void push (ParamID id, ParamValue value) noexcept
	{
		const uint64 w = write.load (std::memory_order_relaxed);
		Slot& s = slots[w & (kSize - 1)];
		s.stamp.store (0, std::memory_order_relaxed);
		std::atomic_thread_fence (std::memory_order_release);
		s.id.store (id, std::memory_order_relaxed);
		s.value.store (value, std::memory_order_relaxed);
		s.stamp.store (w + 1, std::memory_order_release);
		write.store (w + 1, std::memory_order_release);
	}

	bool pop (ParamID& id, ParamValue& value) noexcept
	{
		for (;;)
		{
			const uint64 w = write.load (std::memory_order_acquire);
			if (read == w)
				return false;
			Slot& s = slots[read & (kSize - 1)];
			const uint64 t1 = s.stamp.load (std::memory_order_acquire);
			const ParamID i = s.id.load (std::memory_order_relaxed);
			const ParamValue v = s.value.load (std::memory_order_relaxed);
			std::atomic_thread_fence (std::memory_order_acquire);
			const uint64 t2 = s.stamp.load (std::memory_order_relaxed);
			if (t1 == read + 1 && t2 == read + 1)
			{
				id = i;
				value = v;
				++read;
				return true;
			}
			// This entry was overwritten (or is being): continue with the oldest one that can still be intact.
			const uint64 latest = write.load (std::memory_order_acquire);
			const uint64 oldest = latest >= kSize ? latest - kSize : 0;
			read = oldest > read ? oldest : read + 1;
		}
	}

private:
	struct Slot
	{
		std::atomic<uint64> stamp {0};
		std::atomic<ParamID> id {0};
		std::atomic<ParamValue> value {0.0};
	};
	Slot slots[kSize];
	std::atomic<uint64> write {0};
	uint64 read = 0; // consumer only
};

//------------------------------------------------------------------------------------------------
// Self-test behind the tfv3_selftest export: 0 = all passed, otherwise the number of the failed check.
//------------------------------------------------------------------------------------------------
class SelfTestPoint final : public IConnectionPoint
{
public:
	std::atomic<int> count {0};
	std::string lastId;
	int64 lastInt = -1;
	tresult PLUGIN_API connect (IConnectionPoint*) override { return kResultTrue; }
	tresult PLUGIN_API disconnect (IConnectionPoint*) override { return kResultTrue; }
	tresult PLUGIN_API notify (IMessage* m) override
	{
		lastId = m->getMessageID ();
		int64 v = -1;
		m->getAttributes ()->getInt ("n", v);
		lastInt = v;
		++count;
		return kResultTrue;
	}
	tresult PLUGIN_API queryInterface (const TUID, void** obj) override
	{
		*obj = nullptr;
		return kNoInterface;
	}
	uint32 PLUGIN_API addRef () override { return 1; }
	uint32 PLUGIN_API release () override { return 1; }
};

inline int selfTest ()
{
	// 1. ring: overwrite-oldest keeps the newest kSize entries, in order
	{
		auto ring = std::make_unique<OutParamRing> ();
		for (uint32 i = 0; i < 1000; ++i)
			ring->push (i, i * 0.5);
		ParamID id = 0;
		ParamValue v = 0;
		uint32 expect = 1000 - static_cast<uint32> (OutParamRing::kSize), n = 0;
		while (ring->pop (id, v))
		{
			if (id != expect || v != expect * 0.5)
				return 1;
			++expect;
			++n;
		}
		if (n != OutParamRing::kSize || expect != 1000)
			return 2;
		ring->push (7, 0.25);
		if (!ring->pop (id, v) || id != 7 || v != 0.25 || ring->pop (id, v))
			return 3;
	}
	// 2. a copied message keeps its content when the original changes afterwards
	{
		IPtr<TfMessage> a = owned (new TfMessage ());
		a->setMessageID ("hello");
		a->getAttributes ()->setInt ("n", 41);
		IPtr<TfMessage> b = owned (a->clone ());
		a->getAttributes ()->setInt ("n", 99);
		a->setMessageID ("changed");
		int64 n = 0;
		if (std::string (b->getMessageID ()) != "hello" || b->getAttributes ()->getInt ("n", n) != kResultTrue || n != 41)
			return 4;
		const TChar text[] = {u'o', u'k', 0};
		a->getAttributes ()->setString ("s", text);
		TChar out[8] = {};
		if (a->getAttributes ()->getString ("s", out, sizeof (out)) != kResultTrue || out[0] != u'o' || out[1] != u'k' || out[2] != 0)
			return 5;
	}
	// 3. hub: direct on the delivery thread, queued from another thread (as a copy), from the audio thread without copying
	{
		MessageHub hub;
		SelfTestPoint target;
		hub.toController.target = &target;
		IPtr<TfMessage> m = owned (new TfMessage ());
		m->setMessageID ("direct");
		m->getAttributes ()->setInt ("n", 1);
		if (hub.post (hub.toController, m) != kResultTrue || target.count != 1 || target.lastInt != 1)
			return 6;
		IPtr<TfMessage> q = owned (new TfMessage ());
		q->setMessageID ("queued");
		q->getAttributes ()->setInt ("n", 2);
		tresult fromWorker = kResultFalse;
		std::thread ([&] { fromWorker = hub.post (hub.toController, q); }).join ();
		q->getAttributes ()->setInt ("n", 3); // changed after sending: the queued copy must still say 2
		if (fromWorker != kResultTrue || target.count != 1 || hub.pending (hub.toController) != 1)
			return 7;
		hub.drain ();
		if (target.count != 2 || target.lastId != "queued" || target.lastInt != 2)
			return 8;
		tresult fromAudio = kResultFalse;
		std::thread ([&] { ProcessScope scope; fromAudio = hub.post (hub.toController, m); }).join ();
		if (fromAudio != kResultTrue || target.count != 2)
			return 9;
		hub.drain ();
		if (target.count != 3 || target.lastId != "direct")
			return 10;
		hub.clear ();
	}
	return 0;
}

} // namespace tfv3
