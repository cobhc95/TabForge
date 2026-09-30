// tfvst3 - minimal VST3 host DLL for TabForge's audio engine (see tfvst3.h for the C interface).
// Built on the Steinberg VST3 SDK hosting helpers (MIT licence).

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include "tfvst3.h"

#include "public.sdk/source/common/memorystream.h"
#include "public.sdk/source/vst/hosting/connectionproxy.h"
#include "public.sdk/source/vst/hosting/eventlist.h"
#include "public.sdk/source/vst/hosting/hostclasses.h"
#include "public.sdk/source/vst/hosting/module.h"
#include "public.sdk/source/vst/utility/stringconvert.h"

#include "pluginterfaces/base/funknownimpl.h"
#include "pluginterfaces/gui/iplugview.h"
#include "pluginterfaces/vst/ivstaudioprocessor.h"
#include "pluginterfaces/vst/ivstcomponent.h"
#include "pluginterfaces/vst/ivsteditcontroller.h"
#include "pluginterfaces/vst/ivstevents.h"
#include "pluginterfaces/vst/ivstmessage.h"
#include "pluginterfaces/vst/ivstmidicontrollers.h"
#include "pluginterfaces/vst/ivstparameterchanges.h"
#include "pluginterfaces/vst/ivstprocesscontext.h"
#include "pluginterfaces/vst/vstspeaker.h"

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <atomic>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

using namespace Steinberg;
using namespace Steinberg::Vst;

namespace {

constexpr int32 kMaxEvents = 512;
constexpr int32 kMaxParamQueues = 128;
constexpr int32 kMaxPointsPerQueue = 128;
constexpr int32 kEditRingSize = 1024; // power of two
constexpr int32 kMidiChannels = 16;
constexpr int32 kMidiCtrls = 131; // 0..127 CC, 128 aftertouch, 129 pitch bend, 130 program change

//------------------------------------------------------------------------------------------------
// Small helpers
//------------------------------------------------------------------------------------------------
std::string toUtf8 (const wchar_t* w)
{
	if (!w || !*w)
		return {};
	int n = WideCharToMultiByte (CP_UTF8, 0, w, -1, nullptr, 0, nullptr, nullptr);
	if (n <= 0)
		return {};
	std::string s (static_cast<size_t> (n), '\0');
	WideCharToMultiByte (CP_UTF8, 0, w, -1, &s[0], n, nullptr, nullptr);
	s.resize (static_cast<size_t> (n - 1));
	return s;
}

// Copies UTF-8 into a fixed buffer without splitting a multi-byte sequence.
void copyUtf8 (char* dst, size_t cap, const std::string& src)
{
	if (!dst || cap == 0)
		return;
	size_t n = std::min (src.size (), cap - 1);
	while (n > 0 && n < src.size () && (static_cast<unsigned char> (src[n]) & 0xC0) == 0x80)
		--n; // src[n] is a continuation byte: cut before its lead byte
	std::memcpy (dst, src.data (), n);
	dst[n] = '\0';
}

void setError (char* errorOut, const std::string& msg)
{
	copyUtf8 (errorOut, 512, msg);
}

// Minimal FUnknown boilerplate for objects owned by the plug-in instance (refcount is irrelevant).
#define TFV3_FUNKNOWN_OWNED(Iface)                                                              \
	tresult PLUGIN_API queryInterface (const TUID _iid, void** obj) override                    \
	{                                                                                           \
		if (FUnknownPrivate::iidEqual (_iid, Iface::iid) ||                                     \
		    FUnknownPrivate::iidEqual (_iid, FUnknown::iid))                                    \
		{                                                                                       \
			*obj = static_cast<Iface*> (this);                                                  \
			return kResultOk;                                                                   \
		}                                                                                       \
		*obj = nullptr;                                                                         \
		return kNoInterface;                                                                    \
	}                                                                                           \
	uint32 PLUGIN_API addRef () override { return 1; }                                          \
	uint32 PLUGIN_API release () override { return 1; }

//------------------------------------------------------------------------------------------------
// Fixed-capacity parameter changes (no allocation on the audio thread)
//------------------------------------------------------------------------------------------------
class FixedQueue final : public IParamValueQueue
{
public:
	ParamID id = kNoParamId;
	int32 count = 0;
	struct Point { int32 offset; ParamValue value; } points[kMaxPointsPerQueue];

	ParamID PLUGIN_API getParameterId () override { return id; }
	int32 PLUGIN_API getPointCount () override { return count; }
	tresult PLUGIN_API getPoint (int32 index, int32& sampleOffset, ParamValue& value) override
	{
		if (index < 0 || index >= count)
			return kResultFalse;
		sampleOffset = points[index].offset;
		value = points[index].value;
		return kResultTrue;
	}
	tresult PLUGIN_API addPoint (int32 sampleOffset, ParamValue value, int32& index) override
	{
		int32 dest = count;
		for (int32 i = 0; i < count; ++i)
		{
			if (points[i].offset == sampleOffset)
			{
				points[i].value = value;
				index = i;
				return kResultTrue;
			}
			if (points[i].offset > sampleOffset)
			{
				dest = i;
				break;
			}
		}
		if (count >= kMaxPointsPerQueue)
			return kResultFalse;
		for (int32 i = count; i > dest; --i)
			points[i] = points[i - 1];
		points[dest] = {sampleOffset, value};
		++count;
		index = dest;
		return kResultTrue;
	}
	TFV3_FUNKNOWN_OWNED (IParamValueQueue)
};

class FixedChanges final : public IParameterChanges
{
public:
	FixedQueue queues[kMaxParamQueues];
	int32 used = 0;

	void clear () { used = 0; }
	int32 PLUGIN_API getParameterCount () override { return used; }
	IParamValueQueue* PLUGIN_API getParameterData (int32 index) override
	{
		return (index >= 0 && index < used) ? &queues[index] : nullptr;
	}
	IParamValueQueue* PLUGIN_API addParameterData (const ParamID& pid, int32& index) override
	{
		for (int32 i = 0; i < used; ++i)
		{
			if (queues[i].id == pid)
			{
				index = i;
				return &queues[i];
			}
		}
		if (used >= kMaxParamQueues)
			return nullptr;
		FixedQueue& q = queues[used];
		q.id = pid;
		q.count = 0;
		index = used++;
		return &q;
	}
	void add (ParamID pid, int32 offset, ParamValue value)
	{
		int32 idx = 0;
		if (auto* q = addParameterData (pid, idx))
			q->addPoint (offset, value, idx);
	}
	TFV3_FUNKNOWN_OWNED (IParameterChanges)
};

//------------------------------------------------------------------------------------------------
// Host application object (name only differs from the SDK example)
//------------------------------------------------------------------------------------------------
class TfHostApplication final : public HostApplication
{
public:
	tresult PLUGIN_API getName (String128 name) override
	{
		return StringConvert::convert ("TabForge", name) ? kResultTrue : kInternalError;
	}
};

//------------------------------------------------------------------------------------------------
// Module cache: one loaded Module per path while any instance (or list call) uses it
//------------------------------------------------------------------------------------------------
std::mutex gModuleMutex;
std::map<std::string, std::weak_ptr<VST3::Hosting::Module>> gModules;

VST3::Hosting::Module::Ptr loadModule (const std::string& path, std::string& error)
{
	std::lock_guard<std::mutex> lock (gModuleMutex);
	auto it = gModules.find (path);
	if (it != gModules.end ())
	{
		if (auto m = it->second.lock ())
			return m;
	}
	auto m = VST3::Hosting::Module::create (path, error);
	if (m)
		gModules[path] = m;
	return m;
}

std::vector<VST3::Hosting::ClassInfo> audioClasses (const VST3::Hosting::Module::Ptr& module)
{
	std::vector<VST3::Hosting::ClassInfo> result;
	for (auto& ci : module->getFactory ().classInfos ())
		if (ci.category () == kVstAudioEffectClass)
			result.push_back (ci);
	return result;
}

bool isInstrument (const VST3::Hosting::ClassInfo& ci)
{
	for (auto& s : ci.subCategories ())
		if (s == "Instrument")
			return true;
	return false;
}

struct BusBuffers
{
	std::vector<AudioBusBuffers> buses;
	std::vector<std::vector<float*>> ptrs; // per bus channel pointer arrays
};

} // namespace

//------------------------------------------------------------------------------------------------
// The instance
//------------------------------------------------------------------------------------------------
struct tfv3_plugin
{
	VST3::Hosting::Module::Ptr module;
	IPtr<TfHostApplication> host;
	IPtr<IComponent> component;
	IPtr<IAudioProcessor> processor;
	IPtr<IEditController> controller;
	IPtr<ConnectionProxy> componentCP, controllerCP;
	bool singleComponent = false;
	bool active = false, processing = false;
	int32 processMode = kRealtime;

	double sampleRate = 48000.0;
	int32 maxBlock = 0;
	int64 samplePos = 0;

	BusBuffers ins, outs;
	std::vector<float> zeroBuf, dummyBuf, monoMix;
	int32 mainInChannels = 0, mainOutChannels = 0;

	std::unique_ptr<EventList> events;
	std::unique_ptr<FixedChanges> inParams, outParams;
	ProcessContext context {};

	// State serialised by tfv3_state_begin, held until tfv3_state_copy (main thread only).
	std::vector<uint8_t> pendingState;

	// MIDI controller -> parameter (kNoParamId when unmapped)
	ParamID midiMap[kMidiChannels][kMidiCtrls];

	// Edits from the editor (UI thread) to the processor (audio thread)
	struct Edit { ParamID id; ParamValue value; };
	Edit editRing[kEditRingSize];
	std::atomic<uint32> editWrite {0}, editRead {0};

	// Editor
	IPtr<IPlugView> view;
	HWND parentHwnd = nullptr;
	bool inResize = false;
	int32 hasEditorCache = -1;

	class ComponentHandler final : public IComponentHandler
	{
	public:
		tfv3_plugin* owner = nullptr;
		tresult PLUGIN_API beginEdit (ParamID) override { return kResultOk; }
		tresult PLUGIN_API performEdit (ParamID id, ParamValue value) override
		{
			owner->pushEdit (id, value);
			return kResultOk;
		}
		tresult PLUGIN_API endEdit (ParamID) override { return kResultOk; }
		tresult PLUGIN_API restartComponent (int32 flags) override
		{
			if (flags & kMidiCCAssignmentChanged)
				owner->buildMidiMap ();
			return kResultOk;
		}
		TFV3_FUNKNOWN_OWNED (IComponentHandler)
	} handler;

	class PlugFrame final : public IPlugFrame
	{
	public:
		tfv3_plugin* owner = nullptr;
		tresult PLUGIN_API resizeView (IPlugView* v, ViewRect* newSize) override
		{
			if (!v || !newSize || !owner->parentHwnd)
				return kInvalidArgument;
			if (owner->inResize)
				return kResultFalse;
			owner->inResize = true;
			const int w = newSize->getWidth (), h = newSize->getHeight ();
			HWND parent = owner->parentHwnd;
			HWND root = GetAncestor (parent, GA_ROOT);
			if (root)
			{
				RECT wr {}, cr {};
				GetWindowRect (root, &wr);
				GetClientRect (parent, &cr);
				const int dw = (wr.right - wr.left) - (cr.right - cr.left);
				const int dh = (wr.bottom - wr.top) - (cr.bottom - cr.top);
				SetWindowPos (root, nullptr, 0, 0, w + dw, h + dh,
				              SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
			}
			if (parent != root)
				SetWindowPos (parent, nullptr, 0, 0, w, h, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
			v->onSize (newSize);
			owner->inResize = false;
			return kResultTrue;
		}
		TFV3_FUNKNOWN_OWNED (IPlugFrame)
	} frame;

	tfv3_plugin ()
	{
		handler.owner = this;
		frame.owner = this;
		for (auto& ch : midiMap)
			for (auto& id : ch)
				id = kNoParamId;
	}

	void pushEdit (ParamID id, ParamValue value)
	{
		uint32 w = editWrite.load (std::memory_order_relaxed);
		uint32 r = editRead.load (std::memory_order_acquire);
		if (w - r >= static_cast<uint32> (kEditRingSize))
			return; // full: drop
		editRing[w & (kEditRingSize - 1)] = {id, value};
		editWrite.store (w + 1, std::memory_order_release);
	}

	void buildMidiMap ()
	{
		FUnknownPtr<IMidiMapping> mapping (controller);
		for (int32 ch = 0; ch < kMidiChannels; ++ch)
			for (int32 c = 0; c < kMidiCtrls; ++c)
			{
				ParamID id = kNoParamId;
				if (!mapping ||
				    mapping->getMidiControllerAssignment (0, static_cast<int16> (ch),
				                                          static_cast<CtrlNumber> (c), id) != kResultTrue)
					id = kNoParamId;
				midiMap[ch][c] = id;
			}
	}

	void closeEditor ()
	{
		if (view)
		{
			view->removed ();
			view->setFrame (nullptr);
			view = nullptr;
		}
		parentHwnd = nullptr;
	}

	void shutdown ()
	{
		closeEditor ();
		if (processor && processing)
			processor->setProcessing (false);
		processing = false;
		if (component && active)
			component->setActive (false);
		active = false;

		if (componentCP)
			componentCP->disconnect ();
		if (controllerCP)
			controllerCP->disconnect ();
		componentCP = nullptr;
		controllerCP = nullptr;

		if (controller)
			controller->setComponentHandler (nullptr);
		if (component)
			component->terminate ();
		if (controller && !singleComponent)
			controller->terminate ();

		processor = nullptr;
		controller = nullptr;
		component = nullptr;
		host = nullptr;
		module = nullptr; // last: unloads the DLL when no other instance uses it
	}
};

namespace {

bool setupBuses (tfv3_plugin* p, BusDirection dir, BusBuffers& bb, int32 maxBlock, int32& mainChannels)
{
	const int32 count = p->component->getBusCount (kAudio, dir);
	bb.buses.assign (static_cast<size_t> (count), AudioBusBuffers {});
	bb.ptrs.assign (static_cast<size_t> (count), {});
	mainChannels = 0;
	for (int32 b = 0; b < count; ++b)
	{
		SpeakerArrangement arr = 0;
		int32 channels = 0;
		if (p->processor->getBusArrangement (dir, b, arr) == kResultTrue)
			channels = SpeakerArr::getChannelCount (arr);
		else
		{
			BusInfo info {};
			if (p->component->getBusInfo (kAudio, dir, b, info) == kResultTrue)
				channels = info.channelCount;
		}
		channels = std::max<int32> (channels, 0);
		bb.ptrs[b].assign (static_cast<size_t> (std::max<int32> (channels, 1)), nullptr);
		bb.buses[b].numChannels = channels;
		bb.buses[b].silenceFlags = 0;
		bb.buses[b].channelBuffers32 = bb.ptrs[b].data ();
		if (b == 0)
			mainChannels = channels;
	}
	(void)maxBlock;
	return true;
}

// Serialises component + controller state into one blob: [u32 compSize][comp][u32 ctrlSize][ctrl]. False on failure.
bool serialiseState (tfv3_plugin* p, std::vector<uint8_t>& out)
{
	out.clear ();
	if (!p || !p->component)
		return false;
	MemoryStream comp, ctrl;
	if (p->component->getState (&comp) != kResultOk)
		return false;
	if (p->controller && !p->singleComponent)
		p->controller->getState (&ctrl); // optional
	int64 compSize = 0, ctrlSize = 0;
	comp.tell (&compSize);
	ctrl.tell (&ctrlSize);
	const int64 total = 8 + compSize + ctrlSize;
	if (compSize < 0 || ctrlSize < 0 || total > INT32_MAX)
		return false;
	out.resize (static_cast<size_t> (total));
	uint8_t* w = out.data ();
	const uint32_t cs = static_cast<uint32_t> (compSize), ks = static_cast<uint32_t> (ctrlSize);
	std::memcpy (w, &cs, 4); w += 4;
	if (cs) { std::memcpy (w, comp.getData (), cs); w += cs; }
	std::memcpy (w, &ks, 4); w += 4;
	if (ks) { std::memcpy (w, ctrl.getData (), ks); }
	return true;
}

// Stops processing, deactivates, runs setupProcessing with (mode, rate, block) and reactivates. Main thread, not processing.
// On a refused setup the previous one is restored. True when the new setup was accepted and the plug-in is active again.
bool reSetup (tfv3_plugin* p, int32 mode, double rate, int32 block)
{
	if (p->processing)
		p->processor->setProcessing (false);
	p->processing = false;
	if (p->active)
		p->component->setActive (false);
	p->active = false;
	ProcessSetup setup {};
	setup.processMode = mode;
	setup.symbolicSampleSize = kSample32;
	setup.maxSamplesPerBlock = block;
	setup.sampleRate = rate;
	const bool ok = p->processor->setupProcessing (setup) == kResultOk;
	if (ok)
	{
		p->processMode = mode;
		p->sampleRate = rate;
		if (block != p->maxBlock)
		{
			p->zeroBuf.assign (static_cast<size_t> (block), 0.f);
			p->dummyBuf.assign (static_cast<size_t> (block), 0.f);
			p->monoMix.assign (static_cast<size_t> (block), 0.f);
		}
		p->maxBlock = block;
	}
	else
	{
		setup.processMode = p->processMode;
		setup.maxSamplesPerBlock = p->maxBlock;
		setup.sampleRate = p->sampleRate;
		p->processor->setupProcessing (setup);
	}
	if (p->component->setActive (true) == kResultOk)
		p->active = true;
	p->processor->setProcessing (true); // many plug-ins return kNotImplemented here
	p->processing = true;
	p->samplePos = 0;
	return ok && p->active;
}

} // namespace

//================================================================================================
// C API
//================================================================================================
extern "C" {

TFV3_API int32_t tfv3_list_classes (const wchar_t* path, char (*names)[128], int32_t* is_instrument, int32_t max)
{
	try
	{
		std::string error;
		auto module = loadModule (toUtf8 (path), error);
		if (!module)
			return 0;
		auto classes = audioClasses (module);
		int32_t n = 0;
		for (auto& ci : classes)
		{
			if (n >= max)
				break;
			if (names)
				copyUtf8 (names[n], 128, ci.name ());
			if (is_instrument)
				is_instrument[n] = isInstrument (ci) ? 1 : 0;
			++n;
		}
		return n;
	}
	catch (...)
	{
		return 0;
	}
}


// Diagnostics: with the environment variable TFVST3_TRACE set to a file path, each step of tfv3_create is appended there.
static void traceStep (const char* text)
{
	static const char* path = std::getenv ("TFVST3_TRACE");
	if (!path)
		return;
	if (FILE* f = std::fopen (path, "a"))
	{
		std::fprintf (f, "%s\n", text);
		std::fclose (f);
	}
}
#define TRACE(x) traceStep (x)

TFV3_API tfv3_plugin* tfv3_create (const wchar_t* path, int32_t class_index, double sample_rate, int32_t max_block,
                                  char* error_out)
{
	tfv3_plugin* p = nullptr;
	try
	{
		setError (error_out, "");
		if (max_block <= 0 || sample_rate <= 0)
		{
			setError (error_out, "invalid sample rate or block size");
			return nullptr;
		}
		std::string error;
		TRACE ("loadModule");
		auto module = loadModule (toUtf8 (path), error);
		TRACE ("loadModule done");
		if (!module)
		{
			setError (error_out, error.empty () ? "could not load module" : error);
			return nullptr;
		}
		auto classes = audioClasses (module);
		TRACE ("audioClasses done");
		if (class_index < 0 || class_index >= static_cast<int32_t> (classes.size ()))
		{
			setError (error_out, "class index out of range");
			return nullptr;
		}
		const auto& ci = classes[static_cast<size_t> (class_index)];

		p = new tfv3_plugin ();
		p->module = module;
		p->sampleRate = sample_rate;
		p->maxBlock = max_block;
		p->host = owned (new TfHostApplication ());
		FUnknown* hostCtx = static_cast<IHostApplication*> (p->host.get ());

		auto fail = [&] (const std::string& msg) -> tfv3_plugin* {
			setError (error_out, ci.name () + ": " + msg);
			p->shutdown ();
			delete p;
			return nullptr;
		};

		const auto& factory = module->getFactory ();
		TRACE ("createInstance component");
		p->component = factory.createInstance<IComponent> (ci.ID ());
		TRACE ("createInstance done");
		if (!p->component)
			return fail ("could not create component");
		TRACE ("component initialize");
		if (p->component->initialize (hostCtx) != kResultOk)
		{
			p->component = nullptr; // not initialized: do not terminate
			return fail ("component initialize failed");
		}

		TRACE ("controller setup");
		// controller: single component or separate class
		{
			IEditController* ctrl = nullptr;
			if (p->component->queryInterface (IEditController::iid, reinterpret_cast<void**> (&ctrl)) ==
			        kResultTrue &&
			    ctrl)
			{
				p->controller = owned (ctrl);
				p->singleComponent = true;
			}
			else
			{
				TUID cid;
				if (p->component->getControllerClassId (cid) == kResultTrue)
				{
					p->controller = factory.createInstance<IEditController> (VST3::UID (cid));
					if (p->controller && p->controller->initialize (hostCtx) != kResultOk)
						p->controller = nullptr; // run without controller
				}
			}
		}

		TRACE ("connect controller");
		if (p->controller && !p->singleComponent)
		{
			FUnknownPtr<IConnectionPoint> compCP (p->component);
			FUnknownPtr<IConnectionPoint> ctrlCP (p->controller);
			if (compCP && ctrlCP)
			{
				p->componentCP = owned (new ConnectionProxy (compCP));
				p->controllerCP = owned (new ConnectionProxy (ctrlCP));
				p->componentCP->connect (ctrlCP);
				p->controllerCP->connect (compCP);
			}
			// sync controller with the component's initial state
			MemoryStream stream;
			if (p->component->getState (&stream) == kResultOk)
			{
				stream.seek (0, IBStream::kIBSeekSet, nullptr);
				p->controller->setComponentState (&stream);
			}
		}
		TRACE ("setComponentHandler");
		if (p->controller)
			p->controller->setComponentHandler (&p->handler);

		FUnknownPtr<IAudioProcessor> proc (p->component);
		if (!proc)
			return fail ("component has no IAudioProcessor");
		p->processor = proc;

		if (p->processor->canProcessSampleSize (kSample32) != kResultTrue)
			return fail ("32-bit float processing not supported");

		TRACE ("bus arrangements");
		// bus arrangements: stereo main out (and main in when present); others keep their arrangement
		const int32 numIn = p->component->getBusCount (kAudio, kInput);
		const int32 numOut = p->component->getBusCount (kAudio, kOutput);
		{
			std::vector<SpeakerArrangement> inArr (static_cast<size_t> (std::max (numIn, 1)), SpeakerArr::kStereo);
			std::vector<SpeakerArrangement> outArr (static_cast<size_t> (std::max (numOut, 1)), SpeakerArr::kStereo);
			TRACE ("getBusArrangement in");
			for (int32 i = 0; i < numIn; ++i)
				if (i > 0 && p->processor->getBusArrangement (kInput, i, inArr[i]) != kResultTrue)
					inArr[i] = SpeakerArr::kStereo;
			TRACE ("getBusArrangement out");
			for (int32 i = 0; i < numOut; ++i)
				if (i > 0 && p->processor->getBusArrangement (kOutput, i, outArr[i]) != kResultTrue)
					outArr[i] = SpeakerArr::kStereo;
			TRACE ("setBusArrangements");
			// always a valid array (even for a count of 0): some plug-ins dereference the pointer regardless
			p->processor->setBusArrangements (inArr.data (), numIn, outArr.data (), numOut);
			TRACE ("setBusArrangements done");
			// result ignored: when refused the plug-in keeps its own arrangement, which setupBuses reads back
		}
		TRACE ("activate buses");
		if (numOut > 0)
			p->component->activateBus (kAudio, kOutput, 0, true);
		if (numIn > 0)
			p->component->activateBus (kAudio, kInput, 0, true);
		if (p->component->getBusCount (kEvent, kInput) > 0)
			p->component->activateBus (kEvent, kInput, 0, true);

		TRACE ("setupBuses");
		setupBuses (p, kInput, p->ins, max_block, p->mainInChannels);
		setupBuses (p, kOutput, p->outs, max_block, p->mainOutChannels);

		p->zeroBuf.assign (static_cast<size_t> (max_block), 0.f);
		p->dummyBuf.assign (static_cast<size_t> (max_block), 0.f);
		p->monoMix.assign (static_cast<size_t> (max_block), 0.f);
		p->events.reset (new EventList (kMaxEvents));
		p->inParams.reset (new FixedChanges ());
		p->outParams.reset (new FixedChanges ());

		TRACE ("setupProcessing");
		ProcessSetup setup {};
		setup.processMode = kRealtime;
		setup.symbolicSampleSize = kSample32;
		setup.maxSamplesPerBlock = max_block;
		setup.sampleRate = sample_rate;
		if (p->processor->setupProcessing (setup) != kResultOk)
			return fail ("setupProcessing failed");

		TRACE ("setActive");
		if (p->component->setActive (true) != kResultOk)
			return fail ("setActive failed");
		p->active = true;
		p->processor->setProcessing (true); // many plug-ins return kNotImplemented here
		p->processing = true;

		TRACE ("buildMidiMap");
		if (p->controller)
			p->buildMidiMap ();
		TRACE ("create done");
		return p;
	}
	catch (...)
	{
		setError (error_out, "exception while creating plug-in");
		if (p)
		{
			try { p->shutdown (); } catch (...) {}
			delete p;
		}
		return nullptr;
	}
}

TFV3_API int32_t tfv3_process (tfv3_plugin* p, float** in, int32_t in_channels, float** out, int32_t out_channels,
                             int32_t frames, const tfv3_midi* events, int32_t event_count, double tempo, double ppq_pos,
                             int32_t playing)
{
	return tfv3_process_ex (p, in, in_channels, out, out_channels, frames, events, event_count, tempo, ppq_pos, playing,
	                        0, 0, -1.0);
}

TFV3_API int32_t tfv3_process_ex (tfv3_plugin* p, float** in, int32_t in_channels, float** out, int32_t out_channels,
                                int32_t frames, const tfv3_midi* events, int32_t event_count, double tempo,
                                double ppq_pos, int32_t playing, int32_t time_sig_num, int32_t time_sig_den,
                                double bar_pos_ppq)
{
	auto zeroOut = [&] () {
		if (out && frames > 0)
			for (int32_t c = 0; c < out_channels; ++c)
				if (out[c])
					std::memset (out[c], 0, sizeof (float) * static_cast<size_t> (frames));
	};
	try
	{
		if (!p || !p->processor || frames < 0 || frames > p->maxBlock)
		{
			zeroOut ();
			return 0;
		}
		if (frames == 0)
			return 1;
		const size_t bytes = sizeof (float) * static_cast<size_t> (frames);
		std::memset (p->zeroBuf.data (), 0, bytes);

		// ---- inputs
		for (size_t b = 0; b < p->ins.buses.size (); ++b)
		{
			auto& bus = p->ins.buses[b];
			auto& ptrs = p->ins.ptrs[b];
			bus.silenceFlags = 0;
			for (int32 c = 0; c < bus.numChannels; ++c)
			{
				float* src = p->zeroBuf.data ();
				if (b == 0 && in)
				{
					if (bus.numChannels == 1 && in_channels >= 2 && in[0] && in[1])
					{
						for (int32_t i = 0; i < frames; ++i)
							p->monoMix[i] = 0.5f * (in[0][i] + in[1][i]);
						src = p->monoMix.data ();
					}
					else if (c < in_channels && in[c])
						src = in[c];
					else if (c > 0 && in_channels == 1 && in[0])
						src = in[0]; // mono source into stereo bus
				}
				if (src == p->zeroBuf.data ())
					bus.silenceFlags |= (uint64 (1) << c);
				ptrs[c] = src;
			}
		}
		// ---- outputs
		for (size_t b = 0; b < p->outs.buses.size (); ++b)
		{
			auto& bus = p->outs.buses[b];
			auto& ptrs = p->outs.ptrs[b];
			bus.silenceFlags = 0;
			for (int32 c = 0; c < bus.numChannels; ++c)
				ptrs[c] = (b == 0 && out && c < out_channels && out[c]) ? out[c] : p->dummyBuf.data ();
		}

		// ---- events / parameter changes
		p->events->clear ();
		p->inParams->clear ();
		p->outParams->clear ();

		// edits from the editor
		{
			uint32 r = p->editRead.load (std::memory_order_relaxed);
			const uint32 w = p->editWrite.load (std::memory_order_acquire);
			for (; r != w; ++r)
			{
				const auto& e = p->editRing[r & (kEditRingSize - 1)];
				p->inParams->add (e.id, 0, e.value);
			}
			p->editRead.store (r, std::memory_order_release);
		}

		const bool haveMap = p->controller != nullptr;
		for (int32_t i = 0; events && i < event_count; ++i)
		{
			const tfv3_midi& m = events[i];
			const int32 offset = std::min<int32> (std::max<int32> (m.sample_offset, 0), frames - 1);
			const uint8_t type = m.status & 0xF0;
			const int16 ch = static_cast<int16> (m.status & 0x0F);
			Event ev {};
			ev.busIndex = 0;
			ev.sampleOffset = offset;
			ev.flags = Event::kIsLive;
			switch (type)
			{
				case 0x90:
					if (m.data2 > 0)
					{
						ev.type = Event::kNoteOnEvent;
						ev.noteOn.channel = ch;
						ev.noteOn.pitch = m.data1 & 0x7F;
						ev.noteOn.velocity = (m.data2 & 0x7F) / 127.f;
						ev.noteOn.tuning = 0.f;
						ev.noteOn.length = 0;
						ev.noteOn.noteId = -1;
						p->events->addEvent (ev);
						break;
					}
					[[fallthrough]]; // velocity 0 = note off
				case 0x80:
					ev.type = Event::kNoteOffEvent;
					ev.noteOff.channel = ch;
					ev.noteOff.pitch = m.data1 & 0x7F;
					ev.noteOff.velocity = type == 0x80 ? (m.data2 & 0x7F) / 127.f : 0.f;
					ev.noteOff.noteId = -1;
					ev.noteOff.tuning = 0.f;
					p->events->addEvent (ev);
					break;
				case 0xA0:
					ev.type = Event::kPolyPressureEvent;
					ev.polyPressure.channel = ch;
					ev.polyPressure.pitch = m.data1 & 0x7F;
					ev.polyPressure.pressure = (m.data2 & 0x7F) / 127.f;
					ev.polyPressure.noteId = -1;
					p->events->addEvent (ev);
					break;
				case 0xB0:
					if (haveMap)
					{
						ParamID id = p->midiMap[ch][m.data1 & 0x7F];
						if (id != kNoParamId)
							p->inParams->add (id, offset, (m.data2 & 0x7F) / 127.0);
					}
					break;
				case 0xC0:
					if (haveMap)
					{
						ParamID id = p->midiMap[ch][kCtrlProgramChange];
						if (id != kNoParamId)
							p->inParams->add (id, offset, (m.data1 & 0x7F) / 127.0);
					}
					break;
				case 0xD0:
					if (haveMap)
					{
						ParamID id = p->midiMap[ch][kAfterTouch];
						if (id != kNoParamId)
							p->inParams->add (id, offset, (m.data1 & 0x7F) / 127.0);
					}
					break;
				case 0xE0:
					if (haveMap)
					{
						ParamID id = p->midiMap[ch][kPitchBend];
						if (id != kNoParamId)
						{
							const int v14 = ((m.data2 & 0x7F) << 7) | (m.data1 & 0x7F);
							p->inParams->add (id, offset, v14 / 16383.0);
						}
					}
					break;
				default: break;
			}
		}

		// ---- context
		ProcessContext& ctx = p->context;
		ctx.state = ProcessContext::kTempoValid | ProcessContext::kProjectTimeMusicValid |
		            ProcessContext::kContTimeValid;
		if (playing)
			ctx.state |= ProcessContext::kPlaying;
		ctx.sampleRate = p->sampleRate;
		ctx.tempo = tempo > 0 ? tempo : 120.0;
		ctx.projectTimeMusic = ppq_pos;
		if (time_sig_num > 0 && time_sig_den > 0)
		{
			ctx.state |= ProcessContext::kTimeSigValid;
			ctx.timeSigNumerator = time_sig_num;
			ctx.timeSigDenominator = time_sig_den;
		}
		if (bar_pos_ppq >= 0.0)
		{
			ctx.state |= ProcessContext::kBarPositionValid;
			ctx.barPositionMusic = bar_pos_ppq;
		}
		ctx.projectTimeSamples = p->samplePos;
		ctx.continousTimeSamples = p->samplePos;

		ProcessData data {};
		data.processMode = p->processMode;
		data.symbolicSampleSize = kSample32;
		data.numSamples = frames;
		data.numInputs = static_cast<int32> (p->ins.buses.size ());
		data.numOutputs = static_cast<int32> (p->outs.buses.size ());
		data.inputs = data.numInputs > 0 ? p->ins.buses.data () : nullptr;
		data.outputs = data.numOutputs > 0 ? p->outs.buses.data () : nullptr;
		data.inputParameterChanges = p->inParams.get ();
		data.outputParameterChanges = p->outParams.get ();
		data.inputEvents = p->events.get ();
		data.outputEvents = nullptr;
		data.processContext = &ctx;

		const tresult r = p->processor->process (data);
		p->samplePos += frames;
		if (r != kResultOk)
		{
			zeroOut ();
			return 0;
		}
		// mono plug-in output: duplicate to the right channel
		if (p->mainOutChannels == 1 && out && out_channels >= 2 && out[0] && out[1])
			std::memcpy (out[1], out[0], bytes);
		// caller channels the plug-in does not fill
		if (out)
			for (int32_t c = std::max<int32_t> (p->mainOutChannels, p->mainOutChannels == 1 ? 2 : 0);
			     c < out_channels; ++c)
				if (out[c])
					std::memset (out[c], 0, bytes);
		return 1;
	}
	catch (...)
	{
		zeroOut ();
		return 0;
	}
}

TFV3_API int32_t tfv3_get_state (tfv3_plugin* p, uint8_t* buffer, int32_t capacity)
{
	try
	{
		std::vector<uint8_t> blob;
		if (!serialiseState (p, blob))
			return 0;
		const int32_t total = static_cast<int32_t> (blob.size ());
		if (!buffer)
			return total;
		if (capacity < total)
			return 0;
		std::memcpy (buffer, blob.data (), blob.size ());
		return total;
	}
	catch (...)
	{
		return 0;
	}
}

TFV3_API int32_t tfv3_state_begin (tfv3_plugin* p)
{
	try
	{
		if (!p)
			return 0;
		if (!serialiseState (p, p->pendingState))
		{
			std::vector<uint8_t> ().swap (p->pendingState);
			return 0;
		}
		return static_cast<int32_t> (p->pendingState.size ());
	}
	catch (...)
	{
		if (p)
			std::vector<uint8_t> ().swap (p->pendingState);
		return 0;
	}
}

TFV3_API int32_t tfv3_state_copy (tfv3_plugin* p, uint8_t* buffer, int32_t capacity)
{
	if (!p)
		return 0;
	int32_t result = 0;
	const int32_t size = static_cast<int32_t> (p->pendingState.size ());
	if (buffer && size > 0 && capacity >= size)
	{
		std::memcpy (buffer, p->pendingState.data (), static_cast<size_t> (size));
		result = size;
	}
	std::vector<uint8_t> ().swap (p->pendingState); // always released
	return result;
}

TFV3_API int32_t tfv3_set_state (tfv3_plugin* p, const uint8_t* data, int32_t size)
{
	try
	{
		if (!p || !p->component || !data || size < 8)
			return 0;
		uint32_t cs = 0, ks = 0;
		std::memcpy (&cs, data, 4);
		if (static_cast<int64> (cs) + 8 > size)
			return 0;
		std::memcpy (&ks, data + 4 + cs, 4);
		if (static_cast<int64> (cs) + ks + 8 > size)
			return 0;
		auto* compData = const_cast<uint8_t*> (data + 4);
		auto* ctrlData = const_cast<uint8_t*> (data + 8 + cs);

		MemoryStream comp (compData, cs);
		if (p->component->setState (&comp) != kResultOk)
			return 0;
		if (p->controller)
		{
			MemoryStream comp2 (compData, cs);
			p->controller->setComponentState (&comp2);
			if (ks > 0 && !p->singleComponent)
			{
				MemoryStream ctrl (ctrlData, ks);
				p->controller->setState (&ctrl);
			}
		}
		return 1;
	}
	catch (...)
	{
		return 0;
	}
}

TFV3_API int32_t tfv3_has_editor (tfv3_plugin* p)
{
	try
	{
		if (!p || !p->controller)
			return 0;
		if (p->view)
			return 1;
		if (p->hasEditorCache < 0)
		{
			IPtr<IPlugView> v = owned (p->controller->createView (ViewType::kEditor));
			p->hasEditorCache = (v && v->isPlatformTypeSupported (kPlatformTypeHWND) == kResultTrue) ? 1 : 0;
		}
		return p->hasEditorCache;
	}
	catch (...)
	{
		return 0;
	}
}

TFV3_API int32_t tfv3_open_editor (tfv3_plugin* p, void* parent_hwnd, int32_t* width, int32_t* height)
{
	try
	{
		if (!p || !p->controller || !parent_hwnd)
			return 0;
		p->closeEditor ();
		IPtr<IPlugView> v = owned (p->controller->createView (ViewType::kEditor));
		if (!v || v->isPlatformTypeSupported (kPlatformTypeHWND) != kResultTrue)
			return 0;
		p->parentHwnd = static_cast<HWND> (parent_hwnd);
		v->setFrame (&p->frame);
		if (v->attached (parent_hwnd, kPlatformTypeHWND) != kResultOk)
		{
			v->setFrame (nullptr);
			p->parentHwnd = nullptr;
			return 0;
		}
		p->view = v;
		ViewRect rect {};
		if (v->getSize (&rect) == kResultOk)
		{
			if (width) *width = rect.getWidth ();
			if (height) *height = rect.getHeight ();
		}
		else
		{
			if (width) *width = 0;
			if (height) *height = 0;
		}
		return 1;
	}
	catch (...)
	{
		return 0;
	}
}

TFV3_API void tfv3_close_editor (tfv3_plugin* p)
{
	try
	{
		if (p)
			p->closeEditor ();
	}
	catch (...)
	{
	}
}

TFV3_API int32_t tfv3_latency (tfv3_plugin* p)
{
	try
	{
		if (!p || !p->processor)
			return 0;
		return static_cast<int32_t> (p->processor->getLatencySamples ());
	}
	catch (...)
	{
		return 0;
	}
}

/* Realtime <-> offline (kOffline) processing: setProcessing(false), setActive(false), setupProcessing, setActive(true).
   Main thread only, never while tfv3_process runs. Returns 1 on success. */
TFV3_API int32_t tfv3_set_offline (tfv3_plugin* p, int32_t offline)
{
	try
	{
		if (!p || !p->processor || !p->component)
			return 0;
		int32 mode = offline ? kOffline : kRealtime;
		if (mode == p->processMode)
			return 1;
		return reSetup (p, mode, p->sampleRate, p->maxBlock) ? 1 : 0;
	}
	catch (...)
	{
		return 0;
	}
}

/* New sample rate / max block on a live instance: setActive(false), setupProcessing, setActive(true). Keeps the
   processing mode and all plug-in state. Main thread only, never while tfv3_process runs. Returns 1 on success. */
TFV3_API int32_t tfv3_set_processing (tfv3_plugin* p, double sample_rate, int32_t max_block)
{
	try
	{
		if (!p || !p->processor || !p->component || sample_rate <= 0 || max_block <= 0)
			return 0;
		return reSetup (p, p->processMode, sample_rate, max_block) ? 1 : 0;
	}
	catch (...)
	{
		return 0;
	}
}

/* Tail the plug-in reports, in samples (0 = none, unknown or infinite). */
TFV3_API int32_t tfv3_tail (tfv3_plugin* p)
{
	try
	{
		if (!p || !p->processor)
			return 0;
		uint32 t = p->processor->getTailSamples ();
		if (t == kNoTail || t == kInfiniteTail)
			return 0;
		return t > 0x7fffffffu ? 0x7fffffff : static_cast<int32_t> (t);
	}
	catch (...)
	{
		return 0;
	}
}

TFV3_API void tfv3_destroy (tfv3_plugin* p)
{
	if (!p)
		return;
	try
	{
		p->shutdown ();
	}
	catch (...)
	{
	}
	try
	{
		delete p;
	}
	catch (...)
	{
	}
}

} // extern "C"
