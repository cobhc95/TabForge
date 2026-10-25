/* TabForgeCrashTest.dll - deliberately faulty 64-bit VST2 plug-in for
 * TabForge's plug-in crash-isolation tests. No VST SDK headers are used;
 * the minimal ABI structs are defined by hand below. */
#include <windows.h>
#include <stdint.h>
#include <string.h>

typedef intptr_t (*audioMasterCallback)(void*, int32_t, int32_t, intptr_t, void*, float);
typedef struct AEffect AEffect;
typedef intptr_t (*dispatcherProc)(AEffect*, int32_t, int32_t, intptr_t, void*, float);
typedef void (*processProc)(AEffect*, float**, float**, int32_t);
typedef void (*setParameterProc)(AEffect*, int32_t, float);
typedef float (*getParameterProc)(AEffect*, int32_t);

struct AEffect {
    int32_t magic;
    dispatcherProc dispatcher;
    processProc process;
    setParameterProc setParameter;
    getParameterProc getParameter;
    int32_t numPrograms;
    int32_t numParams;
    int32_t numInputs;
    int32_t numOutputs;
    int32_t flags;
    void* resvd1;
    void* resvd2;
    int32_t initialDelay;
    int32_t realQualities;
    int32_t offQualities;
    float ioRatio;
    void* object;
    void* user;
    int32_t uniqueID;
    int32_t version;
    processProc processReplacing;
    processProc processDoubleReplacing;
    char future[56];
};

#define effFlagsCanReplacing   (1 << 4)
#define effFlagsProgramChunks  (1 << 5)
#define effGetChunk 23
#define effSetChunk 24

static AEffect g_effect;
static int32_t g_mode; /* 0=process(default) 1=load 2=hang 3=none */
static int64_t g_callCount;
static char g_chunk[8] = "TFCRASH1";

static int32_t DetectMode(void) {
    char buf[16];
    DWORD n = GetEnvironmentVariableA("TF_CRASHTEST_MODE", buf, sizeof(buf));
    if (n == 0 || n >= sizeof(buf)) return 0;
    if (_stricmp(buf, "load") == 0) return 1;
    if (_stricmp(buf, "hang") == 0) return 2;
    if (_stricmp(buf, "none") == 0) return 3;
    return 0; /* "process" or unrecognized */
}

static void ProcessReplacing(AEffect* e, float** inputs, float** outputs, int32_t frames) {
    (void)e;
    g_callCount++;
    if (g_mode == 2 && g_callCount > 200) {
        Sleep(INFINITE);
    }
    if (g_mode == 0 && g_callCount > 200) {
        int* p = (int*)0;
        *p = 1; /* access violation */
    }
    for (int32_t ch = 0; ch < 2; ch++) {
        for (int32_t i = 0; i < frames; i++) {
            outputs[ch][i] = inputs[ch][i];
        }
    }
}

static intptr_t Dispatcher(AEffect* e, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt) {
    (void)e; (void)index; (void)value; (void)opt;
    if (opcode == effGetChunk) {
        *(void**)ptr = g_chunk;
        return sizeof(g_chunk);
    }
    if (opcode == effSetChunk) {
        return 0;
    }
    return 0;
}

static void SetParameter(AEffect* e, int32_t index, float value) { (void)e; (void)index; (void)value; }
static float GetParameter(AEffect* e, int32_t index) { (void)e; (void)index; return 0.0f; }

__declspec(dllexport) AEffect* VSTPluginMain(audioMasterCallback host) {
    (void)host;
    g_mode = DetectMode();

    if (g_mode == 1) {
        int* p = (int*)0;
        *p = 1; /* access violation inside VSTPluginMain */
    }

    memset(&g_effect, 0, sizeof(g_effect));
    g_effect.magic = 0x56737450; /* 'VstP' */
    g_effect.dispatcher = Dispatcher;
    g_effect.process = NULL;
    g_effect.setParameter = SetParameter;
    g_effect.getParameter = GetParameter;
    g_effect.numPrograms = 1;
    g_effect.numParams = 0;
    g_effect.numInputs = 2;
    g_effect.numOutputs = 2;
    g_effect.flags = effFlagsCanReplacing | effFlagsProgramChunks;
    g_effect.uniqueID = 0x54466374; /* 'TFct' */
    g_effect.version = 1;
    g_effect.processReplacing = ProcessReplacing;
    g_effect.processDoubleReplacing = NULL;

    return &g_effect;
}
