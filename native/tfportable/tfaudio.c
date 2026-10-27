// tfportable audio: miniaudio behind the plain C ABI of tfportable.h. See that header.
#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_GENERATION
#define MA_NO_ENGINE
#define MA_NO_NODE_GRAPH
#define MA_NO_RESOURCE_MANAGER
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"
#include "tfportable.h"
#include <stdlib.h>
#include <string.h>

int tf_abi_version(void) { return TF_ABI_VERSION; }

typedef struct { ma_context context; } tf_context;

typedef struct
{
    ma_device device;
    tf_audio_data_cb data;
    tf_audio_event_cb event;
    void* user;
    int capture;
} tf_stream;

void* tf_audio_context_create(int useNullBackend)
{
    tf_context* c = (tf_context*)calloc(1, sizeof(tf_context));
    if (!c) return NULL;
    ma_backend nullOnly[] = { ma_backend_null };
    ma_result r = useNullBackend ? ma_context_init(nullOnly, 1, NULL, &c->context) : ma_context_init(NULL, 0, NULL, &c->context);
    if (r != MA_SUCCESS) { free(c); return NULL; }
    return c;
}

void tf_audio_context_destroy(void* context)
{
    tf_context* c = (tf_context*)context;
    if (!c) return;
    ma_context_uninit(&c->context);
    free(c);
}

static void copy_name(const char* from, char* to, int capacity)
{
    if (!to || capacity <= 0) return;
    strncpy(to, from ? from : "", (size_t)capacity - 1);
    to[capacity - 1] = 0;
}

int tf_audio_backend_name(void* context, char* name, int capacity)
{
    tf_context* c = (tf_context*)context;
    if (!c) return -1;
    copy_name(ma_get_backend_name(c->context.backend), name, capacity);
    return 0;
}

static ma_result list_devices(tf_context* c, int isCapture, ma_device_info** infos, ma_uint32* count)
{
    ma_device_info* playback = NULL; ma_device_info* capture = NULL; ma_uint32 pc = 0, cc = 0;
    ma_result r = ma_context_get_devices(&c->context, &playback, &pc, &capture, &cc);
    if (r != MA_SUCCESS) return r;
    *infos = isCapture ? capture : playback;
    *count = isCapture ? cc : pc;
    return MA_SUCCESS;
}

int tf_audio_device_count(void* context, int isCapture)
{
    tf_context* c = (tf_context*)context;
    ma_device_info* infos; ma_uint32 count;
    if (!c || list_devices(c, isCapture, &infos, &count) != MA_SUCCESS) return 0;
    return (int)count;
}

int tf_audio_device_info(void* context, int isCapture, int index, char* name, int capacity, int* isDefault, int* maxChannels, int* nativeRate)
{
    tf_context* c = (tf_context*)context;
    ma_device_info* infos; ma_uint32 count;
    if (!c || list_devices(c, isCapture, &infos, &count) != MA_SUCCESS || index < 0 || (ma_uint32)index >= count) return -1;
    copy_name(infos[index].name, name, capacity);
    if (isDefault) *isDefault = infos[index].isDefault ? 1 : 0;
    if (maxChannels) *maxChannels = 0;
    if (nativeRate) *nativeRate = 0;
    ma_device_info full;
    if (ma_context_get_device_info(&c->context, isCapture ? ma_device_type_capture : ma_device_type_playback, &infos[index].id, &full) == MA_SUCCESS)
    {
        ma_uint32 most = 0;
        for (ma_uint32 i = 0; i < full.nativeDataFormatCount; i++)
        {
            if (full.nativeDataFormats[i].channels > most) most = full.nativeDataFormats[i].channels;
            if (i == 0 && nativeRate) *nativeRate = (int)full.nativeDataFormats[i].sampleRate;
        }
        if (maxChannels) *maxChannels = (int)most;
    }
    return 0;
}

static void on_data(ma_device* device, void* output, const void* input, ma_uint32 frames)
{
    tf_stream* s = (tf_stream*)device->pUserData;
    s->data(s->user, (float*)output, (const float*)input, frames);
}

static void on_event(const ma_device_notification* n)
{
    tf_stream* s = (tf_stream*)n->pDevice->pUserData;
    int reason = 0;
    switch (n->type)
    {
    case ma_device_notification_type_stopped: reason = 1; break;
    case ma_device_notification_type_rerouted: reason = 2; break;
    case ma_device_notification_type_interruption_began: reason = 3; break;
    case ma_device_notification_type_interruption_ended: reason = 4; break;
    default: return;
    }
    if (s->event) s->event(s->user, reason);
}

void* tf_audio_stream_open(void* context, int isCapture, const char* deviceName, int channels, int sampleRate, int periodFrames,
                           tf_audio_data_cb data, tf_audio_event_cb event, void* user,
                           int* actualChannels, int* actualRate, int* latencyFrames, int* error)
{
    tf_context* c = (tf_context*)context;
    if (error) *error = 0;
    if (!c || !data) { if (error) *error = MA_INVALID_ARGS; return NULL; }
    tf_stream* s = (tf_stream*)calloc(1, sizeof(tf_stream));
    if (!s) { if (error) *error = MA_OUT_OF_MEMORY; return NULL; }
    s->data = data; s->event = event; s->user = user; s->capture = isCapture;

    ma_device_config cfg = ma_device_config_init(isCapture ? ma_device_type_capture : ma_device_type_playback);
    ma_device_id id;
    ma_device_id* chosen = NULL;
    if (deviceName && deviceName[0])
    {
        ma_device_info* infos; ma_uint32 count;
        if (list_devices(c, isCapture, &infos, &count) == MA_SUCCESS)
            for (ma_uint32 i = 0; i < count; i++)
                if (strcmp(infos[i].name, deviceName) == 0) { id = infos[i].id; chosen = &id; break; }
    }
    if (isCapture)
    {
        cfg.capture.pDeviceID = chosen; cfg.capture.format = ma_format_f32; cfg.capture.channels = (ma_uint32)channels;
    }
    else
    {
        cfg.playback.pDeviceID = chosen; cfg.playback.format = ma_format_f32; cfg.playback.channels = (ma_uint32)channels;
    }
    cfg.sampleRate = (ma_uint32)sampleRate;
    cfg.periodSizeInFrames = periodFrames > 0 ? (ma_uint32)periodFrames : 0;
    cfg.performanceProfile = ma_performance_profile_low_latency;
    cfg.dataCallback = on_data;
    cfg.notificationCallback = on_event;
    cfg.pUserData = s;

    ma_result r = ma_device_init(&c->context, &cfg, &s->device);
    if (r != MA_SUCCESS) { if (error) *error = (int)r; free(s); return NULL; }
    ma_uint32 rate = s->device.sampleRate;
    if (actualRate) *actualRate = (int)rate;
    if (actualChannels) *actualChannels = (int)(isCapture ? s->device.capture.channels : s->device.playback.channels);
    if (latencyFrames)
    {
        ma_uint32 internalPeriod = isCapture ? s->device.capture.internalPeriodSizeInFrames : s->device.playback.internalPeriodSizeInFrames;
        ma_uint32 periods = isCapture ? s->device.capture.internalPeriods : s->device.playback.internalPeriods;
        ma_uint32 internalRate = isCapture ? s->device.capture.internalSampleRate : s->device.playback.internalSampleRate;
        *latencyFrames = internalRate ? (int)((ma_uint64)internalPeriod * periods * rate / internalRate) : 0;
    }
    return s;
}

int tf_audio_stream_start(void* stream) { tf_stream* s = (tf_stream*)stream; return s ? (int)ma_device_start(&s->device) : -1; }
int tf_audio_stream_stop(void* stream) { tf_stream* s = (tf_stream*)stream; return s ? (int)ma_device_stop(&s->device) : -1; }

void tf_audio_stream_close(void* stream)
{
    tf_stream* s = (tf_stream*)stream;
    if (!s) return;
    ma_device_uninit(&s->device);
    free(s);
}
