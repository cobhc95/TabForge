// tfv3_params.inc - the parameter exports of the tfvst3 shim (generic plug-in controls panel).
// Included by tfvst3.cpp inside its extern "C" block (after tfv3_tail), so it sees tfv3_plugin and TFV3_API; not compiled on its own.
// All of these run on the engine's main thread (the plug-in's controller thread), like tfv3_editor_idle. Values are VST3 normalized (0..1).
// tfv3_param_set also queues the value for the processor through the same edit ring the editor uses, so the audio thread picks it up without locks.

// UTF-16 (VST3 String128 / TChar) to UTF-8 in `out` (capacity bytes, always terminated); an unpaired surrogate becomes U+FFFD; stops when full.
static void tfv3_to_utf8 (const TChar* s, int32 maxUnits, char* out, int32 capacity)
{
	if (!out || capacity <= 0)
		return;
	int32 n = 0;
	for (int32 i = 0; s && i < maxUnits && s[i]; ++i)
	{
		uint32_t c = static_cast<uint16_t> (s[i]);
		if (c >= 0xD800 && c <= 0xDBFF && i + 1 < maxUnits && static_cast<uint16_t> (s[i + 1]) >= 0xDC00 &&
		    static_cast<uint16_t> (s[i + 1]) <= 0xDFFF)
		{
			c = 0x10000 + ((c - 0xD800) << 10) + (static_cast<uint16_t> (s[i + 1]) - 0xDC00);
			++i;
		}
		else if (c >= 0xD800 && c <= 0xDFFF)
			c = 0xFFFD;
		char b[4];
		int32 len = 0;
		if (c < 0x80)
			b[len++] = static_cast<char> (c);
		else if (c < 0x800)
		{
			b[len++] = static_cast<char> (0xC0 | (c >> 6));
			b[len++] = static_cast<char> (0x80 | (c & 0x3F));
		}
		else if (c < 0x10000)
		{
			b[len++] = static_cast<char> (0xE0 | (c >> 12));
			b[len++] = static_cast<char> (0x80 | ((c >> 6) & 0x3F));
			b[len++] = static_cast<char> (0x80 | (c & 0x3F));
		}
		else
		{
			b[len++] = static_cast<char> (0xF0 | (c >> 18));
			b[len++] = static_cast<char> (0x80 | ((c >> 12) & 0x3F));
			b[len++] = static_cast<char> (0x80 | ((c >> 6) & 0x3F));
			b[len++] = static_cast<char> (0x80 | (c & 0x3F));
		}
		if (n + len >= capacity)
			break;
		for (int32 k = 0; k < len; ++k)
			out[n++] = b[k];
	}
	out[n] = 0;
}

TFV3_API int32_t tfv3_param_count (tfv3_plugin* p)
{
	try
	{
		return p && p->controller ? p->controller->getParameterCount () : 0;
	}
	catch (...)
	{
		return 0;
	}
}

/* Parameter `index` (0 .. tfv3_param_count - 1). name: 256 bytes UTF-8, units: 64 bytes UTF-8. step_count 0 = continuous, n = n + 1 discrete values.
   flags: 1 read-only, 2 hidden, 4 program change, 8 bypass. Any out pointer may be null. Returns 1 on success. */
TFV3_API int32_t tfv3_param_info (tfv3_plugin* p, int32_t index, uint32_t* id, char* name, char* units, int32_t* step_count,
                                  int32_t* flags, double* default_normalized, double* value_normalized)
{
	try
	{
		if (!p || !p->controller || index < 0)
			return 0;
		ParameterInfo info {};
		if (p->controller->getParameterInfo (index, info) != kResultOk)
			return 0;
		if (id)
			*id = info.id;
		tfv3_to_utf8 (info.title, 128, name, 256);
		tfv3_to_utf8 (info.units, 128, units, 64);
		if (step_count)
			*step_count = info.stepCount < 0 ? 0 : info.stepCount;
		if (flags)
			*flags = ((info.flags & ParameterInfo::kIsReadOnly) ? 1 : 0) | ((info.flags & ParameterInfo::kIsHidden) ? 2 : 0) |
			         ((info.flags & ParameterInfo::kIsProgramChange) ? 4 : 0) | ((info.flags & ParameterInfo::kIsBypass) ? 8 : 0);
		if (default_normalized)
			*default_normalized = info.defaultNormalizedValue;
		if (value_normalized)
			*value_normalized = p->controller->getParamNormalized (info.id);
		return 1;
	}
	catch (...)
	{
		return 0;
	}
}

/* The plug-in's own text for a normalized value ("-6.0"), in `out` (capacity bytes UTF-8). Returns 1 on success. */
TFV3_API int32_t tfv3_param_text (tfv3_plugin* p, uint32_t id, double normalized, char* out, int32_t capacity)
{
	try
	{
		if (!p || !p->controller || !out || capacity <= 0)
			return 0;
		String128 text {};
		if (p->controller->getParamStringByValue (id, normalized, text) != kResultOk)
			return 0;
		tfv3_to_utf8 (text, 128, out, capacity);
		return 1;
	}
	catch (...)
	{
		return 0;
	}
}

/* Sets a normalized value (clamped to 0..1) on the controller and queues it for the processor. Returns 1 on success. */
TFV3_API int32_t tfv3_param_set (tfv3_plugin* p, uint32_t id, double normalized)
{
	try
	{
		if (!p || !p->controller || !(normalized == normalized))
			return 0;
		const ParamValue v = normalized < 0 ? 0 : (normalized > 1 ? 1 : normalized);
		if (p->controller->setParamNormalized (id, v) != kResultOk)
			return 0;
		p->pushEdit (id, v);
		return 1;
	}
	catch (...)
	{
		return 0;
	}
}
