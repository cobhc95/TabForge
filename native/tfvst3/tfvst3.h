/* tfvst3 — minimal C interface to host VST3 plug-ins (built on the Steinberg VST3 SDK, MIT licence).
   Used only by TabForge's audio engine process (C# P/Invoke). All functions are called from the engine:
   tfv3_process on the audio thread, everything else on the engine's main (UI) thread.
   Every function returns 0 / a null handle on failure and never throws across the C boundary. */
#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
#define TFV3_API __declspec(dllexport)

typedef struct tfv3_plugin tfv3_plugin; /* one plug-in instance (component + controller) */

typedef struct {
    int32_t sample_offset;  /* frame within the block */
    uint8_t status, data1, data2, pad;
} tfv3_midi;

/* Describes the audio-processor classes in a .vst3 bundle or file (instruments and effects).
   Writes up to max entries into names (each 128 bytes UTF-8) and is_instrument; returns the count. */
TFV3_API int32_t tfv3_list_classes(const wchar_t* path, char (*names)[128], int32_t* is_instrument, int32_t max);

/* Loads class `class_index` of the module, sets up stereo out (and stereo in for effects), activates it and
   starts processing. Returns null on failure (error text in error_out, 512 bytes UTF-8). */
TFV3_API tfv3_plugin* tfv3_create(const wchar_t* path, int32_t class_index, double sample_rate, int32_t max_block,
                                  char* error_out);

/* Processes one block: in/out are arrays of channel pointers (non-interleaved float); inputs may be null for
   instruments. Midi events: note on/off become VST3 note events, CC / pitch bend / program go through the
   IMidiMapping parameters when the plug-in offers them. Returns 1 on success. */
TFV3_API int32_t tfv3_process(tfv3_plugin* p, float** in, int32_t in_channels, float** out, int32_t out_channels,
                             int32_t frames, const tfv3_midi* events, int32_t event_count, double tempo, double ppq_pos,
                             int32_t playing);
/* tfv3_process plus the musical position: time signature (both > 0 to be valid) and the ppq position of the current
   bar's start (>= 0 to be valid); invalid values leave the matching ProcessContext flag clear. */
TFV3_API int32_t tfv3_process_ex(tfv3_plugin* p, float** in, int32_t in_channels, float** out, int32_t out_channels,
                                int32_t frames, const tfv3_midi* events, int32_t event_count, double tempo,
                                double ppq_pos, int32_t playing, int32_t time_sig_num, int32_t time_sig_den,
                                double bar_pos_ppq);

/* Saved state (component state + controller state in one blob). Preferred: tfv3_state_begin serialises once and
   returns the size (0 = no state / failure), tfv3_state_copy copies that blob into buffer (returns the size, or 0 when
   capacity is too small / nothing is pending) and always frees it; pass a null buffer to just free it.
   Legacy: tfv3_get_state returns the size needed when buffer is null (serialises on every call).
   tfv3_set_state returns 0 when the blob is malformed or the plug-in's component rejects it. */
TFV3_API int32_t tfv3_state_begin(tfv3_plugin* p);
TFV3_API int32_t tfv3_state_copy(tfv3_plugin* p, uint8_t* buffer, int32_t capacity);
TFV3_API int32_t tfv3_get_state(tfv3_plugin* p, uint8_t* buffer, int32_t capacity);
TFV3_API int32_t tfv3_set_state(tfv3_plugin* p, const uint8_t* data, int32_t size);

/* Editor: attaches the plug-in view (kPlatformTypeHWND) to parent_hwnd; returns 1 and the preferred size. */
TFV3_API int32_t tfv3_has_editor(tfv3_plugin* p);
TFV3_API int32_t tfv3_open_editor(tfv3_plugin* p, void* parent_hwnd, int32_t* width, int32_t* height);
TFV3_API void    tfv3_close_editor(tfv3_plugin* p);

/* Latency the plug-in reports, in samples. */
TFV3_API int32_t tfv3_latency(tfv3_plugin* p);

/* Offline (kOffline) or realtime processing mode; main thread only, while not processing. Returns 1 on success. */
TFV3_API int32_t tfv3_set_offline(tfv3_plugin* p, int32_t offline);
/* New sample rate / max block on a live instance (deactivate, setupProcessing, reactivate; state kept); main thread
   only, while not processing. Returns 1 on success; on 0 the old setup is restored when the plug-in allows it. */
TFV3_API int32_t tfv3_set_processing(tfv3_plugin* p, double sample_rate, int32_t max_block);
/* Tail the plug-in reports, in samples (0 = none, unknown or infinite). */
TFV3_API int32_t tfv3_tail(tfv3_plugin* p);

/* Stops processing, deactivates, terminates and frees everything (safe with null). */
TFV3_API void tfv3_destroy(tfv3_plugin* p);

#ifdef __cplusplus
}
#endif
