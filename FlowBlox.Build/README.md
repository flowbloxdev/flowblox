# Shared FlowBlox build inputs

This directory contains source-controlled application data and shared build scripts.
Generated binaries belong in each project's output directory.

- `data/`: bundled examples, auxiliary projects, prompts, fonts, HTML and Python scripts.
- `FlowBlox.Build.targets`: imported by WinForms, Avalonia and CLI. Copies `data/` to
  each application's build and publish directories using `PreserveNewest`.
- `postbuild.bat` and `postbuild_onnx.ps1`: copy ONNX runtimes to the corresponding
  output directory. Runtime source caches remain in `FlowBloxResources/data` and
  are prepared using `FlowBloxResources/Sync-OnnxRuntimes.ps1`.

Import `..\FlowBlox.Build\FlowBlox.Build.targets` from another application project to
use the same inputs. Scripts are build tools and are not copied into the application;
Python scripts inside `data/` are application data and are copied.

The former `SkipFlowBloxAppDir=true` build option continues to skip ONNX copying.
Bundled application data is always included.
