// tfportable: the thin C ABI TabForge's non-Windows (and opt-in dev) audio and MIDI backends call.
// Audio: miniaudio (public domain / MIT-0). MIDI: RtMidi (MIT-style). Everything here is a plain C function with
// simple types so the managed side needs no struct layouts: it loads the library with NativeLibrary and calls through
// function pointers. Not shipped in the Windows build.
#ifndef TFPORTABLE_H
#define TFPORTABLE_H

#ifdef _WIN32
#define TF_API __declspec(dllexport)
#else
#define TF_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define TF_ABI_VERSION 1

TF_API int tf_abi_version(void);

/* ---- Audio (miniaudio) ---- */

/* Called on the device's real-time thread. Playback: fill output (frames x channels, f32 interleaved). Capture: read input. */
typedef void (*tf_audio_data_cb)(void* user, float* output, const float* input, unsigned int frames);
/* reason: 1 stopped, 2 rerouted (default device changed), 3 interruption began, 4 interruption ended. */
typedef void (*tf_audio_event_cb)(void* user, int reason);

/* useNullBackend != 0: only miniaudio's null backend (no hardware; used by tests). NULL on failure. */
TF_API void* tf_audio_context_create(int useNullBackend);
TF_API void tf_audio_context_destroy(void* context);
TF_API int tf_audio_backend_name(void* context, char* name, int capacity);
TF_API int tf_audio_device_count(void* context, int isCapture);
/* Fills the UTF-8 name; maxChannels is 0 when the device does not say. Returns 0 on success. */
TF_API int tf_audio_device_info(void* context, int isCapture, int index, char* name, int capacity, int* isDefault, int* maxChannels, int* nativeRate);

/* deviceName NULL or empty: the default device. channels/sampleRate 0: the device's own. Returns a stream or NULL (error code in *error). */
TF_API void* tf_audio_stream_open(void* context, int isCapture, const char* deviceName, int channels, int sampleRate, int periodFrames,
                                  tf_audio_data_cb data, tf_audio_event_cb event, void* user,
                                  int* actualChannels, int* actualRate, int* latencyFrames, int* error);
TF_API int tf_audio_stream_start(void* stream);
TF_API int tf_audio_stream_stop(void* stream);
TF_API void tf_audio_stream_close(void* stream);

/* ---- MIDI (RtMidi) ---- */

/* Called on the MIDI driver thread with one complete message (sysex, clock and active sensing are ignored). */
typedef void (*tf_midi_message_cb)(void* user, const unsigned char* message, int length, double deltaSeconds);

TF_API int tf_midi_in_count(void);
TF_API int tf_midi_in_name(int index, char* name, int capacity);
TF_API void* tf_midi_in_open(int index, tf_midi_message_cb callback, void* user);
TF_API void tf_midi_in_close(void* port);

TF_API int tf_midi_out_count(void);
TF_API int tf_midi_out_name(int index, char* name, int capacity);
TF_API void* tf_midi_out_open(int index);
TF_API int tf_midi_out_send(void* port, const unsigned char* message, int length);
TF_API void tf_midi_out_close(void* port);

#ifdef __cplusplus
}
#endif
#endif
