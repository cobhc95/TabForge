// tfportable MIDI: RtMidi behind the plain C ABI of tfportable.h. See that header.
#include "tfportable.h"
#include "RtMidi.h"
#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace
{
    struct InPort
    {
        std::unique_ptr<RtMidiIn> midi;
        tf_midi_message_cb callback = nullptr;
        void* user = nullptr;
    };

    void copyName(const std::string& from, char* to, int capacity)
    {
        if (!to || capacity <= 0) return;
        std::strncpy(to, from.c_str(), static_cast<size_t>(capacity) - 1);
        to[capacity - 1] = 0;
    }

    void onMessage(double delta, std::vector<unsigned char>* message, void* user)
    {
        auto* port = static_cast<InPort*>(user);
        if (port->callback && message && !message->empty()) port->callback(port->user, message->data(), static_cast<int>(message->size()), delta);
    }

    // The default RtMidi API of the platform (WinMM, CoreMIDI, ALSA sequencer); a failure while probing means "no devices".
    template <typename Port> int portCount()
    {
        try { Port probe; return static_cast<int>(probe.getPortCount()); }
        catch (RtMidiError&) { return 0; }
    }

    template <typename Port> int portName(int index, char* name, int capacity)
    {
        try
        {
            Port probe;
            if (index < 0 || static_cast<unsigned>(index) >= probe.getPortCount()) return -1;
            copyName(probe.getPortName(static_cast<unsigned>(index)), name, capacity);
            return 0;
        }
        catch (RtMidiError&) { return -1; }
    }
}

extern "C"
{
    int tf_midi_in_count(void) { return portCount<RtMidiIn>(); }
    int tf_midi_in_name(int index, char* name, int capacity) { return portName<RtMidiIn>(index, name, capacity); }

    void* tf_midi_in_open(int index, tf_midi_message_cb callback, void* user)
    {
        try
        {
            auto port = std::make_unique<InPort>();
            port->midi = std::make_unique<RtMidiIn>();
            if (index < 0 || static_cast<unsigned>(index) >= port->midi->getPortCount()) return nullptr;
            port->callback = callback;
            port->user = user;
            port->midi->ignoreTypes(true, true, true);   // sysex, clock and active sensing are not used
            port->midi->setCallback(&onMessage, port.get());
            port->midi->openPort(static_cast<unsigned>(index), "TabForge");
            return port.release();
        }
        catch (RtMidiError&) { return nullptr; }
    }

    void tf_midi_in_close(void* handle)
    {
        auto* port = static_cast<InPort*>(handle);
        if (!port) return;
        try { port->midi->cancelCallback(); port->midi->closePort(); } catch (RtMidiError&) { }
        delete port;
    }

    int tf_midi_out_count(void) { return portCount<RtMidiOut>(); }
    int tf_midi_out_name(int index, char* name, int capacity) { return portName<RtMidiOut>(index, name, capacity); }

    void* tf_midi_out_open(int index)
    {
        try
        {
            auto out = std::make_unique<RtMidiOut>();
            if (index < 0 || static_cast<unsigned>(index) >= out->getPortCount()) return nullptr;
            out->openPort(static_cast<unsigned>(index), "TabForge");
            return out.release();
        }
        catch (RtMidiError&) { return nullptr; }
    }

    int tf_midi_out_send(void* handle, const unsigned char* message, int length)
    {
        auto* out = static_cast<RtMidiOut*>(handle);
        if (!out || !message || length <= 0) return -1;
        try { out->sendMessage(message, static_cast<size_t>(length)); return 0; }
        catch (RtMidiError&) { return -1; }
    }

    void tf_midi_out_close(void* handle)
    {
        auto* out = static_cast<RtMidiOut*>(handle);
        if (!out) return;
        try { out->closePort(); } catch (RtMidiError&) { }
        delete out;
    }
}
