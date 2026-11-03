"""Download a supported large CUDA model package for ONNX Runtime GenAI."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path


DEFAULT_MODEL_ROOT = Path.home() / ".flowblox" / "onnx" / "genai"
DEFAULT_MODEL_ID = "microsoft/mistral-7b-instruct-v0.2-ONNX"
MODELS = {
    "microsoft/mistral-7b-instruct-v0.2-ONNX": {
        "remote_folder": "onnx/cuda/mistral-7b-instruct-v0.2-cuda-int4-rtn-block-32",
        "folder": "Mistral-7B-Instruct-v0.2-onnx-cuda-int4",
        "files": (
            "genai_config.json",
            "tokenizer.json",
            "mistral-7b-instruct-v0.2-cuda-int4-rtn-block-32.onnx",
            "mistral-7b-instruct-v0.2-cuda-int4-rtn-block-32.onnx.data",
        ),
        "minimum_vram": 8192,
        "preferred_vram": 12288,
        "size": "about 4.3 GB",
    },
    "microsoft/Phi-3-medium-4k-instruct-onnx-cuda": {
        "remote_folder": "cuda-int4-rtn-block-32",
        "folder": "Phi-3-medium-4k-instruct-onnx-cuda-int4",
        "files": (
            "genai_config.json",
            "tokenizer.json",
            "phi3-medium-4k-instruct-cuda-int4-rtn-block-32.onnx",
            "phi3-medium-4k-instruct-cuda-int4-rtn-block-32.onnx.data",
        ),
        "minimum_vram": 16384,
        "preferred_vram": 16384,
        "size": "large 14B package",
    },
    "onnxruntime/DeepSeek-R1-Distill-ONNX/deepseek-r1-distill-qwen-7B": {
        "repository": "onnxruntime/DeepSeek-R1-Distill-ONNX",
        "remote_folder": "deepseek-r1-distill-qwen-7B/gpu/gpu-int4-rtn-block-32",
        "folder": "DeepSeek-R1-Distill-Qwen-7B-onnx-cuda-int4",
        "files": ("genai_config.json", "tokenizer.json", "model.onnx", "model.onnx.data"),
        "minimum_vram": 8192,
        "preferred_vram": 12288,
        "size": "about 5.1 GB",
    },
}


def ensure_huggingface_hub():
    try:
        from huggingface_hub import snapshot_download
    except ImportError:
        print("Installing the Hugging Face download client...", flush=True)
        subprocess.check_call([sys.executable, "-m", "pip", "install", "--upgrade", "huggingface_hub"])
        from huggingface_hub import snapshot_download
    return snapshot_download


def require_cuda(minimum_vram: int, preferred_vram: int, skip_check: bool) -> None:
    if skip_check:
        return
    executable = shutil.which("nvidia-smi")
    if not executable:
        raise RuntimeError("CUDA check failed: nvidia-smi was not found.")
    result = subprocess.run(
        [executable, "--query-gpu=name,memory.total,compute_cap", "--format=csv,noheader,nounits"],
        capture_output=True,
        text=True,
        timeout=15,
    )
    candidates = []
    for line in result.stdout.splitlines() if result.returncode == 0 else ():
        values = [value.strip() for value in line.split(",")]
        try:
            candidates.append((values[0], int(values[1]), float(values[2])))
        except (IndexError, ValueError):
            pass
    suitable = [gpu for gpu in candidates if gpu[1] >= minimum_vram and gpu[2] >= 7.0]
    if not suitable:
        raise RuntimeError(
            f"This model is not recommended here. It requires Compute Capability 7.0+ "
            f"and at least {minimum_vram // 1024} GiB VRAM."
        )
    gpu = max(suitable, key=lambda item: item[1])
    print(f"CUDA hardware: {gpu[0]}, {gpu[1]} MiB VRAM, Compute Capability {gpu[2]:g}", flush=True)
    if gpu[1] < preferred_vram:
        print(f"WARNING: {preferred_vram // 1024} GiB+ VRAM is preferred for stable performance.", flush=True)


def parse_args():
    parser = argparse.ArgumentParser(description="Download a supported large CUDA model for FlowBlox.")
    parser.add_argument(
        "--model-id",
        choices=tuple(MODELS),
        default=DEFAULT_MODEL_ID,
        help=f"Supported model preset (default: {DEFAULT_MODEL_ID}).",
    )
    parser.add_argument("--list-models", action="store_true", help="List model IDs without downloading.")
    parser.add_argument("--output-directory", "--target", dest="output_directory", type=Path)
    parser.add_argument("--model-root-directory", type=Path, default=DEFAULT_MODEL_ROOT)
    parser.add_argument(
        "--skip-hardware-check",
        action="store_true",
        help="Allow staging for another machine; never use to bypass incompatible local hardware.",
    )
    args = parser.parse_args()
    return args


def main() -> None:
    args = parse_args()
    if args.list_models:
        for model_id, model in MODELS.items():
            print(f"{model_id} | {model['size']} | minimum {model['minimum_vram'] // 1024} GiB VRAM")
        return

    model = MODELS[args.model_id]
    repository = model.get("repository", args.model_id)
    target = (args.output_directory or args.model_root_directory / model["folder"]).expanduser().resolve()
    if all((target / name).is_file() for name in model["files"]):
        print(f"Model is already available in: {target}", flush=True)
        print(f"MODEL_FOLDER={target}", flush=True)
        return

    require_cuda(model["minimum_vram"], model["preferred_vram"], args.skip_hardware_check)
    staging = target.parent / f".{target.name}-download"
    target.mkdir(parents=True, exist_ok=True)
    print(f"Downloading {args.model_id} ({model['size']}) to {target}...", flush=True)
    ensure_huggingface_hub()(
        repo_id=repository,
        allow_patterns=f"{model['remote_folder']}/*",
        local_dir=staging,
    )
    downloaded_folder = staging / Path(model["remote_folder"])
    if not downloaded_folder.is_dir():
        raise RuntimeError(f"No model files found below {repository}/{model['remote_folder']}")
    for source_file in downloaded_folder.rglob("*"):
        if source_file.is_file():
            destination = target / source_file.relative_to(downloaded_folder)
            destination.parent.mkdir(parents=True, exist_ok=True)
            source_file.replace(destination)
    missing = [name for name in model["files"] if not (target / name).is_file()]
    if missing:
        raise RuntimeError(f"Download incomplete; missing: {', '.join(missing)}")
    shutil.rmtree(staging, ignore_errors=True)
    print(f"MODEL_FOLDER={target}", flush=True)
    print("MODEL_READY=true", flush=True)


if __name__ == "__main__":
    main()
