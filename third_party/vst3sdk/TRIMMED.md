# Trimmed VST3 SDK

Pinned: v3.8.1_build_84, commit 3cdf9ca5d1f5b1b21e0a86832aa4abe55607bd96
(submodules: base fcf9da0bd27a16f7f03773a3a39822f28f5c8477, pluginterfaces 4f547e8e102b47de4a8b8aaf343c73b700786372, public.sdk 586dc5e6c8012c3e4b01c79389375cbe96bdb1da).

Only base/, pluginterfaces/, public.sdk/source/ and cmake/ are kept: that is all TabForge's native shims compile.
Samples, docs, vstgui4 and the submodule .git history were moved to
..\..\..\..\TabForge-archive\vst3sdk-unused (27 Oct 2026). fetch-vst3sdk.ps1 accepts this copy by the pin above;
run it with -Force to re-fetch the full SDK from Steinberg.
