// tfv3test - console smoke test for tfvst3.dll. Loads the DLL dynamically (like the C# P/Invoke side does),
// lists classes, runs an effect over a sine wave and an instrument with a note-on, and round-trips state.
// Usage: tfv3test [effect.vst3] [instrument.vst3] [note]      (never opens editors)
#include <windows.h>
#include <objbase.h>

#include "tfvst3.h"

#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <string>
#include <vector>

namespace fs = std::filesystem;

static struct Api
{
	decltype (&tfv3_list_classes) list_classes;
	decltype (&tfv3_create) create;
	decltype (&tfv3_process) process;
	decltype (&tfv3_get_state) get_state;
	decltype (&tfv3_set_state) set_state;
	decltype (&tfv3_has_editor) has_editor;
	decltype (&tfv3_latency) latency;
	decltype (&tfv3_destroy) destroy;
	decltype (&tfv3_process_ex) process_ex;
	decltype (&tfv3_state_begin) state_begin;
	decltype (&tfv3_state_copy) state_copy;
	decltype (&tfv3_set_processing) set_processing;
} api;

static int gFailures = 0;
static void expect (bool ok, const char* what)
{
	std::printf ("  [%s] %s\n", ok ? "PASS" : "FAIL", what);
	if (!ok)
		++gFailures;
}

static bool loadApi ()
{
	wchar_t exe[MAX_PATH];
	GetModuleFileNameW (nullptr, exe, MAX_PATH);
	fs::path dll = fs::path (exe).parent_path () / L"tfvst3.dll";
	HMODULE h = LoadLibraryW (dll.c_str ());
	if (!h)
	{
		std::printf ("cannot load %ls (error %lu)\n", dll.c_str (), GetLastError ());
		return false;
	}
#define GET(n) api.n = reinterpret_cast<decltype (api.n)> (GetProcAddress (h, "tfv3_" #n)); if (!api.n) { std::printf ("missing export tfv3_%s\n", #n); return false; }
	GET (list_classes) GET (create) GET (process) GET (get_state) GET (set_state) GET (has_editor) GET (latency)
	GET (destroy) GET (process_ex) GET (state_begin) GET (state_copy) GET (set_processing)
#undef GET
	return true;
}

static int listClasses (const fs::path& path, std::vector<int>& instrument)
{
	char names[64][128];
	int32_t inst[64];
	int n = api.list_classes (path.c_str (), names, inst, 64);
	std::printf ("  %ls: %d class(es)\n", path.filename ().c_str (), n);
	instrument.assign (inst, inst + n);
	for (int i = 0; i < n; ++i)
		std::printf ("    [%d] %s%s\n", i, names[i], inst[i] ? "  (instrument)" : "");
	return n;
}

static void stateRoundTrip (tfv3_plugin* p)
{
	int32_t size = api.get_state (p, nullptr, 0);
	std::vector<uint8_t> buf (static_cast<size_t> (size > 0 ? size : 0) + 4096);
	int32_t written = size > 0 ? api.get_state (p, buf.data (), static_cast<int32_t> (buf.size ())) : 0;
	uint32_t cs = 0, ks = 0;
	if (written >= 8)
	{
		std::memcpy (&cs, buf.data (), 4);
		std::memcpy (&ks, buf.data () + 4 + cs, 4);
	}
	int32_t ok = written > 0 ? api.set_state (p, buf.data (), written) : 0;
	std::printf ("  state: size query %d, written %d (component %u + controller %u bytes), set_state -> %d\n",
	             size, written, cs, ks, ok);

	// Single serialise: begin returns the size, copy copies + frees; a second copy finds nothing pending.
	int32_t begun = api.state_begin (p);
	std::vector<uint8_t> once (static_cast<size_t> (begun > 0 ? begun : 0));
	int32_t copied = begun > 0 ? api.state_copy (p, once.data (), begun) : 0;
	expect (begun > 0 && copied == begun, "state_begin/state_copy: one serialise, copy returns the begun size");
	expect (api.state_copy (p, once.data (), begun) == 0, "state_copy: the buffer is freed after the first copy");
	expect (copied > 0 && api.set_state (p, once.data (), copied) == 1, "set_state accepts the single-serialise blob");
	// Too-small buffer: 0, and the pending blob is still freed.
	if (api.state_begin (p) > 1)
		expect (api.state_copy (p, once.data (), 1) == 0 && api.state_copy (p, once.data (), begun) == 0,
		        "state_copy: too-small capacity returns 0 and frees the pending blob");
	// Rejections are reported (0), never silently accepted.
	uint8_t truncated[8] = {0xFF, 0xFF, 0x00, 0x00, 0, 0, 0, 0}; // component size 65535 > blob
	expect (api.set_state (p, truncated, 8) == 0, "set_state: a malformed (truncated) blob returns 0");
	expect (api.set_state (p, nullptr, 0) == 0, "set_state: an empty blob returns 0");
}

// Live reconfigure (R-01 follow-up) and the musical-position process call (RT-04).
static void reconfigureAndMeter (tfv3_plugin* p, bool instrument)
{
	expect (api.set_processing (p, 44100.0, 512) == 1, "set_processing 44.1 kHz / 512 on a live instance");
	std::vector<float> inL (512), inR (512), outL (512), outR (512);
	float* in[2] = {inL.data (), inR.data ()};
	float* out[2] = {outL.data (), outR.data ()};
	int ok = 0;
	double ppq = 0;
	for (int b = 0; b < 20; ++b)
	{
		const double bar = std::floor (ppq / 3.0) * 3.0; // 3/4: a bar is three quarter notes
		ok += api.process_ex (p, instrument ? nullptr : in, instrument ? 0 : 2, out, 2, 512, nullptr, 0, 120.0, ppq, 1, 3, 4, bar);
		ppq += 512 / 44100.0 * 2.0;
	}
	expect (ok == 20, "process_ex (3/4 + bar position) at the new 512 block size");
	expect (api.process_ex (p, instrument ? nullptr : in, instrument ? 0 : 2, out, 2, 513, nullptr, 0, 120.0, 0, 1, 4, 4, 0) == 0,
	        "process_ex refuses a block above the new max block");
	expect (api.set_processing (p, 48000.0, 128) == 1, "set_processing back down to 48 kHz / 128");
	expect (api.process_ex (p, instrument ? nullptr : in, instrument ? 0 : 2, out, 2, 256, nullptr, 0, 120.0, 0, 1, 4, 4, 0) == 0 &&
	            api.process_ex (p, instrument ? nullptr : in, instrument ? 0 : 2, out, 2, 128, nullptr, 0, 120.0, 0, 1, 6, 8, 0) == 1,
	        "after shrinking: 256 refused, 128 processed (6/8)");
	expect (api.set_processing (p, 0.0, 128) == 0 && api.set_processing (p, 48000.0, 0) == 0, "set_processing rejects a zero rate / block");
	expect (api.set_processing (p, 48000.0, 256) == 1, "set_processing restores the test setup");
}

static const double kRate = 48000.0;
static const int kBlock = 256;

static void runEffect (const fs::path& path, int classIndex)
{
	char err[512] = {};
	auto t0 = std::chrono::steady_clock::now ();
	tfv3_plugin* p = api.create (path.c_str (), classIndex, kRate, kBlock, err);
	auto t1 = std::chrono::steady_clock::now ();
	if (!p)
	{
		std::printf ("  create FAILED: %s\n", err);
		return;
	}
	std::printf ("  created in %.0f ms, latency %d samples, has_editor %d\n",
	             std::chrono::duration<double, std::milli> (t1 - t0).count (), api.latency (p), api.has_editor (p));

	std::vector<float> inL (kBlock), inR (kBlock), outL (kBlock), outR (kBlock);
	float* in[2] = {inL.data (), inR.data ()};
	float* out[2] = {outL.data (), outR.data ()};
	double inSum = 0, outSum = 0, sumL = 0, sumR = 0;
	int okBlocks = 0;
	double phase = 0, ppq = 0;
	double procMs = 0;
	for (int b = 0; b < 100; ++b)
	{
		for (int i = 0; i < kBlock; ++i)
		{
			float s = 0.5f * static_cast<float> (std::sin (phase));
			phase += 2.0 * 3.14159265358979 * 440.0 / kRate;
			inL[i] = inR[i] = s;
			inSum += 2.0 * s * s;
		}
		auto a = std::chrono::steady_clock::now ();
		okBlocks += api.process (p, in, 2, out, 2, kBlock, nullptr, 0, 120.0, ppq, 1);
		procMs += std::chrono::duration<double, std::milli> (std::chrono::steady_clock::now () - a).count ();
		ppq += kBlock / kRate * 2.0;
		for (int i = 0; i < kBlock; ++i)
		{
			sumL += double (outL[i]) * outL[i];
			sumR += double (outR[i]) * outR[i];
		}
	}
	outSum = sumL + sumR;
	const double n = 100.0 * kBlock * 2;
	std::printf ("  100 blocks: %d ok, input RMS %.4f, output RMS %.4f (L %.4f / R %.4f), avg process %.3f ms/block\n",
	             okBlocks, std::sqrt (inSum / n), std::sqrt (outSum / n), std::sqrt (sumL / (n / 2)),
	             std::sqrt (sumR / (n / 2)), procMs / 100.0);
	stateRoundTrip (p);
	reconfigureAndMeter (p, false);
	api.destroy (p);
	std::printf ("  destroyed\n");
}

static void runInstrument (const fs::path& path, int classIndex, int note)
{
	char err[512] = {};
	auto t0 = std::chrono::steady_clock::now ();
	tfv3_plugin* p = api.create (path.c_str (), classIndex, kRate, kBlock, err);
	auto t1 = std::chrono::steady_clock::now ();
	if (!p)
	{
		std::printf ("  create FAILED: %s\n", err);
		return;
	}
	std::printf ("  created in %.0f ms, latency %d samples, has_editor %d\n",
	             std::chrono::duration<double, std::milli> (t1 - t0).count (), api.latency (p), api.has_editor (p));
	std::vector<float> outL (kBlock), outR (kBlock);
	float* out[2] = {outL.data (), outR.data ()};
	double ppq = 0;
	// Optional warm-up (env TFV3TEST_WARMUP seconds): process silence in real time while pumping window messages,
	// for plug-ins that finish loading samples asynchronously / on the message thread.
	if (const char* wu = std::getenv ("TFV3TEST_WARMUP"))
	{
		const int blocks = static_cast<int> (std::atof (wu) * kRate / kBlock);
		const bool pump = std::getenv ("TFV3TEST_NOPUMP") == nullptr;
		for (int b = 0; b < blocks; ++b)
		{
			MSG msg;
			while (pump && PeekMessageW (&msg, nullptr, 0, 0, PM_REMOVE))
			{
				TranslateMessage (&msg);
				DispatchMessageW (&msg);
			}
			api.process (p, nullptr, 0, out, 2, kBlock, nullptr, 0, 120.0, ppq, 0);
			Sleep (5);
		}
		std::printf ("  warm-up: %d silent blocks%s\n", blocks, pump ? " with message pumping" : " (no message pumping)");
	}
	// 4 windows of 100 blocks (~0.53 s each); note-on at the start of windows 0 and 2 (sample loading may be async),
	// note-off at the start of windows 1 and 3.
	for (int w = 0; w < 4; ++w)
	{
		double sum = 0;
		int okBlocks = 0;
		for (int b = 0; b < 100; ++b)
		{
			tfv3_midi ev {};
			int count = 0;
			if (b == 0)
			{
				ev.sample_offset = 0;
				ev.status = (w % 2 == 0) ? 0x90 : 0x80;
				ev.data1 = static_cast<uint8_t> (note);
				ev.data2 = 100;
				count = 1;
			}
			okBlocks += api.process (p, nullptr, 0, out, 2, kBlock, count ? &ev : nullptr, count, 120.0, ppq, 1);
			ppq += kBlock / kRate * 2.0;
			for (int i = 0; i < kBlock; ++i)
				sum += double (outL[i]) * outL[i] + double (outR[i]) * outR[i];
		}
		std::printf ("  window %d (%s note %d at start): %d ok, output RMS %.4f\n", w,
		             (w % 2 == 0) ? "note-on vel 100" : "note-off", note, okBlocks, std::sqrt (sum / (100.0 * kBlock * 2)));
	}
	stateRoundTrip (p);
	reconfigureAndMeter (p, true);
	api.destroy (p);
	std::printf ("  destroyed\n");
}

static std::vector<fs::path> findVst3 (const fs::path& dir, bool recursive)
{
	std::vector<fs::path> r;
	std::error_code ec;
	if (!fs::exists (dir, ec))
		return r;
	if (recursive)
	{
		for (auto it = fs::recursive_directory_iterator (dir, ec); it != fs::recursive_directory_iterator (); it.increment (ec))
			if (_wcsicmp (it->path ().extension ().c_str (), L".vst3") == 0)
			{
				r.push_back (it->path ());
				if (it->is_directory ())
					it.disable_recursion_pending ();
			}
	}
	else
		for (auto& e : fs::directory_iterator (dir, ec))
			if (_wcsicmp (e.path ().extension ().c_str (), L".vst3") == 0)
				r.push_back (e.path ());
	return r;
}

int wmain (int argc, wchar_t** argv)
{
	CoInitializeEx (nullptr, COINIT_APARTMENTTHREADED);
	if (!loadApi ())
		return 1;
	const fs::path vst3Root = L"C:\\Program Files\\Common Files\\VST3";

	// ---- effects
	std::vector<fs::path> effects;
	if (argc > 1)
		effects.push_back (argv[1]);
	else
	{
		effects = findVst3 (vst3Root / L"ValhallaDSP", false);
		if (effects.empty ())
			effects = findVst3 (vst3Root, true);
	}
	std::printf ("== Effects: listing classes\n");
	fs::path effectPath;
	int effectClass = -1;
	for (auto& e : effects)
	{
		std::vector<int> inst;
		int n = listClasses (e, inst);
		for (int i = 0; i < n && effectClass < 0; ++i)
			if (!inst[i])
			{
				effectPath = e;
				effectClass = i;
			}
	}
	if (effectClass >= 0)
	{
		std::printf ("== Effect instance: %ls class %d @ %.0f Hz / %d\n", effectPath.filename ().c_str (), effectClass,
		             kRate, kBlock);
		runEffect (effectPath, effectClass);
	}
	else
		std::printf ("no effect class found\n");

	// ---- instrument
	fs::path instPath = argc > 2 ? fs::path (argv[2]) : vst3Root / L"Nexus.vst3";
	std::error_code ec;
	if (fs::exists (instPath, ec))
	{
		std::printf ("== Instrument: listing classes\n");
		std::vector<int> inst;
		int n = listClasses (instPath, inst);
		int cls = -1;
		for (int i = 0; i < n && cls < 0; ++i)
			if (inst[i])
				cls = i;
		if (cls < 0 && n > 0)
			cls = 0;
		if (cls >= 0)
		{
			std::printf ("== Instrument instance: %ls class %d\n", instPath.filename ().c_str (), cls);
			runInstrument (instPath, cls, argc > 3 ? _wtoi (argv[3]) : 60);
		}
	}
	else
		std::printf ("instrument %ls not present\n", instPath.c_str ());

	std::printf ("== done: %d check failure(s)\n", gFailures);
	CoUninitialize ();
	return gFailures == 0 ? 0 : 2;
}
