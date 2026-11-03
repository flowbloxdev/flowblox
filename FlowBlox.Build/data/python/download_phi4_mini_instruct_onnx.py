"""Download the CPU INT4 Phi-4 Mini Instruct package for ONNX Runtime GenAI."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path


REPOSITORY = "microsoft/Phi-4-mini-instruct-onnx"
REMOTE_FOLDER = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4"
MODEL_FOLDER_NAME = REPOSITORY.rsplit("/", maxsplit=1)[-1]
DEFAULT_MODEL_ROOT = Path.home() / ".flowblox" / "onnx" / "genai"
REQUIRED_FILES = (
    "genai_config.json",
    "tokenizer.json",
    "model.onnx",
    "model.onnx.data",
)


def is_complete_model(target: Path) -> bool:
    return all((target / name).is_file() for name in REQUIRED_FILES)


def ensure_huggingface_hub():
    try:
        from huggingface_hub import snapshot_download
    except ImportError:
        print("Installing the Hugging Face download client...", flush=True)
        subprocess.check_call(
            [sys.executable, "-m", "pip", "install", "--upgrade", "huggingface_hub"]
        )
        from huggingface_hub import snapshot_download

    return snapshot_download


def parse_args():
    parser = argparse.ArgumentParser(
        description="Download the CPU INT4 Phi-4 Mini Instruct model for FlowBlox ONNX GenAI."
    )
    parser.add_argument(
        "--output-directory",
        "--target",
        dest="output_directory",
        type=Path,
        help="Exact destination model directory. Overrides --model-root-directory.",
    )
    parser.add_argument(
        "--model-root-directory",
        type=Path,
        default=DEFAULT_MODEL_ROOT,
        help=(
            "Root directory below which Phi-4-mini-instruct-onnx is stored "
            f"(default: {DEFAULT_MODEL_ROOT})."
        ),
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    target = args.output_directory or (args.model_root_directory / MODEL_FOLDER_NAME)
    target = target.expanduser().resolve()
    if is_complete_model(target):
        print(f"Phi-4 Mini Instruct is already available in: {target}", flush=True)
        return

    staging = target.parent / f".{target.name}-download"
    target.mkdir(parents=True, exist_ok=True)

    snapshot_download = ensure_huggingface_hub()
    print(f"Phi-4 Mini Instruct is about 4.9 GB. Downloading to {target}...", flush=True)
    snapshot_download(
        repo_id=REPOSITORY,
        allow_patterns=f"{REMOTE_FOLDER}/*",
        local_dir=staging,
    )

    downloaded_folder = staging / Path(REMOTE_FOLDER)
    if not downloaded_folder.is_dir():
        raise RuntimeError(f"No model files found below {REPOSITORY}/{REMOTE_FOLDER}")

    for source_file in downloaded_folder.rglob("*"):
        if not source_file.is_file():
            continue
        relative_path = source_file.relative_to(downloaded_folder)
        destination = target / relative_path
        destination.parent.mkdir(parents=True, exist_ok=True)
        source_file.replace(destination)

    missing = [name for name in REQUIRED_FILES if not (target / name).is_file()]
    if missing:
        raise RuntimeError(f"Download incomplete; missing: {', '.join(missing)}")

    shutil.rmtree(staging, ignore_errors=True)
    print(f"Phi-4 Mini Instruct is ready in: {target}", flush=True)
    print("Select this directory as ModelFolder in the ONNX GenAI FlowBlock.", flush=True)


if __name__ == "__main__":
    main()
