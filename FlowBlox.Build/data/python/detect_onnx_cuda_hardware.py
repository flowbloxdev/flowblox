"""Report whether this machine is a suitable candidate for FlowBlox CUDA models."""

from __future__ import annotations

import json
import shutil
import subprocess


def main() -> None:
    executable = shutil.which("nvidia-smi")
    if not executable:
        print(json.dumps({
            "recommendedProvider": "Default",
            "cudaAvailable": False,
            "reason": "nvidia-smi was not found; no usable NVIDIA CUDA installation was detected.",
            "gpus": [],
        }))
        return

    command = [
        executable,
        "--query-gpu=name,memory.total,compute_cap,driver_version",
        "--format=csv,noheader,nounits",
    ]
    result = subprocess.run(command, capture_output=True, text=True, timeout=15)
    if result.returncode != 0:
        print(json.dumps({
            "recommendedProvider": "Default",
            "cudaAvailable": False,
            "reason": result.stderr.strip() or "nvidia-smi failed.",
            "gpus": [],
        }))
        return

    gpus = []
    for line in result.stdout.splitlines():
        values = [value.strip() for value in line.split(",")]
        if len(values) != 4:
            continue
        gpus.append({
            "name": values[0],
            "memoryMiB": int(values[1]),
            "computeCapability": values[2],
            "driverVersion": values[3],
        })

    max_memory = max((gpu["memoryMiB"] for gpu in gpus), default=0)
    capabilities = []
    for gpu in gpus:
        try:
            capabilities.append(float(gpu["computeCapability"]))
        except ValueError:
            pass
    capability_ok = bool(capabilities) and max(capabilities) >= 7.0
    print(json.dumps({
        "recommendedProvider": "CUDA" if gpus else "Default",
        "cudaAvailable": bool(gpus),
        "reason": "NVIDIA CUDA hardware and driver detected." if gpus else "No NVIDIA GPU was reported.",
        "gpus": gpus,
        "modelRecommendations": {
            "mistral7bCudaInt4": {
                "recommended": capability_ok and max_memory >= 8192,
                "guidance": "Compute Capability 7.0+ and at least 8 GiB VRAM; 12 GiB+ is preferable.",
            },
            "phi3Medium14bCudaInt4": {
                "recommended": capability_ok and max_memory >= 16384,
                "guidance": "Compute Capability 7.0+ and 16 GiB+ VRAM recommended for stable use.",
            },
            "deepSeekR1DistillQwen7bCudaInt4": {
                "recommended": capability_ok and max_memory >= 8192,
                "guidance": "Compute Capability 7.0+ and at least 8 GiB VRAM; 12 GiB+ is preferable.",
            },
        },
    }))


if __name__ == "__main__":
    main()
