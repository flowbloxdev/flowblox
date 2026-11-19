"""Download a CPU or GPU INT4 Phi-4 Mini Instruct package for ONNX Runtime GenAI."""

from __future__ import annotations

import argparse
import os
import shutil
import stat
import subprocess
import sys
from pathlib import Path


REPOSITORY = "microsoft/Phi-4-mini-instruct-onnx"
REMOTE_FOLDER = "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4"
GPU_REMOTE_FOLDER = "gpu/gpu-int4-rtn-block-32"
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
        environment = os.environ.copy()
        # PyYAML's optional C extension may lack wheels for newer Python versions.
        environment.setdefault("PYYAML_FORCE_LIBYAML", "0")
        subprocess.check_call(
            [sys.executable, "-m", "pip", "install", "--upgrade", "--prefer-binary", "huggingface_hub"],
            env=environment,
        )
        from huggingface_hub import snapshot_download

    return snapshot_download


def normalize_provider(value: str) -> str:
    return "cpu" if value.lower() == "default" else value.lower()


def ensure_model_link(link: Path | None, target: Path) -> None:
    if link is None:
        return
    link = link.expanduser().absolute()
    if os.path.lexists(link):
        if link.exists() and os.path.samefile(link, target):
            return
        if link.is_symlink():
            link.unlink()
        elif sys.platform == "win32" and link.lstat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            os.rmdir(link)  # Remove the junction only; never delete its model directory.
        else:
            raise RuntimeError(f"Model link path already exists and is not a link: {link}")
    link.parent.mkdir(parents=True, exist_ok=True)
    if sys.platform == "win32":
        subprocess.check_call(["cmd", "/d", "/c", "mklink", "/J", str(link), str(target)])
    else:
        link.symlink_to(target, target_is_directory=True)


def parse_args():
    parser = argparse.ArgumentParser(
        description="Download the Phi-4 Mini Instruct model for FlowBlox ONNX GenAI."
    )
    parser.add_argument(
        "--provider",
        type=normalize_provider,
        choices=("cpu", "cuda", "directml"),
        default="cpu",
        help="CPU (default), NVIDIA CUDA, or AMD/Windows DirectML; GPU providers share one package.",
    )
    parser.add_argument(
        "--model-directory-link",
        type=Path,
        help="Optional project-local directory link to the selected central CPU/GPU model; reuses weights without copying.",
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
    gpu = args.provider != "cpu"
    if args.provider == "directml" and sys.platform != "win32":
        raise RuntimeError("DirectML requires Windows and a compatible GPU driver.")
    model_folder = MODEL_FOLDER_NAME + ("-gpu" if gpu else "")
    remote_folder = GPU_REMOTE_FOLDER if gpu else REMOTE_FOLDER
    target = args.output_directory or (args.model_root_directory / model_folder)
    target = target.expanduser().resolve()
    if is_complete_model(target):
        ensure_model_link(args.model_directory_link, target)
        print(f"Phi-4 Mini Instruct is already available in: {target}", flush=True)
        return

    staging = target.parent / f".{target.name}-download"
    target.mkdir(parents=True, exist_ok=True)

    snapshot_download = ensure_huggingface_hub()
    print(f"Downloading Phi-4 Mini Instruct ({args.provider}, several GB) to {target}...", flush=True)
    snapshot_download(
        repo_id=REPOSITORY,
        allow_patterns=f"{remote_folder}/*",
        local_dir=staging,
    )

    downloaded_folder = staging / Path(remote_folder)
    if not downloaded_folder.is_dir():
        raise RuntimeError(f"No model files found below {REPOSITORY}/{remote_folder}")

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
    ensure_model_link(args.model_directory_link, target)
    print(f"Phi-4 Mini Instruct is ready in: {target}", flush=True)
    print("Select this directory as ModelFolder in the ONNX GenAI FlowBlock.", flush=True)


if __name__ == "__main__":
    main()
