"""Provision multilingual Qwen3-4B-Instruct-2507 INT4 for ONNX Runtime GenAI."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import stat
import subprocess
import sys

PACKAGES = {
    "cpu": ("robinsmits/qwen3-4b-instruct-2507-onnx-int4", "97d8690faf0cad28279dd10334a67c79f673a57f"),
    "directml": ("angelitasr/Qwen3-4B-Instruct-2507-DML", "1378d9bf9c82af68ef14a2d34e07058ac2d2fa58"),
    "cuda": ("dandyruse/qwen3-4b-instruct-2507-int4-cuda", "e20322ab53a685ebf8d89c905923070d8bdfa620"),
}
REQUIRED_FILES = ("genai_config.json", "model.onnx", "model.onnx.data", "tokenizer.json", "tokenizer_config.json")
DOWNLOAD_FILES = (*REQUIRED_FILES, "added_tokens.json", "special_tokens_map.json", "vocab.json", "merges.txt", "chat_template.jinja", "config.json", "generation_config.json")
DEFAULT_MODEL_ROOT = Path.home() / ".flowblox" / "onnx" / "genai"
MODEL_NAME = "Qwen3-4B-Instruct-2507-ONNX"


def normalize_provider(value: str) -> str:
    value = value.strip().lower()
    return "cpu" if value == "default" else value


def is_complete_model(target: Path) -> bool:
    return all((target / name).is_file() and (target / name).stat().st_size > 0 for name in REQUIRED_FILES)


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
            os.rmdir(link)  # Remove only the junction, never the model directory.
        else:
            raise RuntimeError(f"Model link path already exists and is not a link: {link}")
    link.parent.mkdir(parents=True, exist_ok=True)
    if sys.platform == "win32":
        environment = os.environ.copy()
        environment["FLOWBLOX_MODEL_LINK"] = str(link)
        environment["FLOWBLOX_MODEL_TARGET"] = str(target)
        subprocess.check_call(
            'mklink /J "%FLOWBLOX_MODEL_LINK%" "%FLOWBLOX_MODEL_TARGET%"',
            shell=True,
            env=environment,
        )
    else:
        link.symlink_to(target, target_is_directory=True)


def download_model(target: Path, repository: str, revision: str) -> None:
    # Use HTTPS rather than requiring the optional Xet transport or a PyYAML C extension.
    os.environ.setdefault("HF_HUB_DISABLE_XET", "1")
    os.environ.setdefault("HF_HUB_DOWNLOAD_TIMEOUT", "60")
    try:
        from huggingface_hub import snapshot_download
    except ImportError:
        environment = os.environ.copy()
        environment.setdefault("PYYAML_FORCE_LIBYAML", "0")
        subprocess.check_call(
            [sys.executable, "-m", "pip", "install", "--prefer-binary", "huggingface_hub"],
            env=environment,
        )
        from huggingface_hub import snapshot_download
    print(f"Downloading {MODEL_NAME} from {repository} (several GB)...", flush=True)
    snapshot_download(
        repo_id=repository,
        revision=revision,
        allow_patterns=list(DOWNLOAD_FILES),
        local_dir=target,
        max_workers=2,
    )
    if not is_complete_model(target):
        raise RuntimeError(f"Incomplete ONNX Runtime GenAI model: {target}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--provider", type=normalize_provider, choices=tuple(PACKAGES), default="cpu")
    parser.add_argument("--model-root-directory", type=Path, default=DEFAULT_MODEL_ROOT)
    parser.add_argument("--model-directory-link", type=Path)
    parser.add_argument("--output-directory", "--target", dest="output_directory", type=Path)
    args = parser.parse_args()
    if args.provider == "directml" and sys.platform != "win32":
        raise RuntimeError("DirectML requires Windows and a compatible GPU driver.")
    repository, revision = PACKAGES[args.provider]
    target = (args.output_directory or args.model_root_directory / f"{MODEL_NAME}-{args.provider}").expanduser().resolve()
    if not is_complete_model(target):
        download_model(target, repository, revision)
    configuration = json.loads((target / "genai_config.json").read_text(encoding="utf-8-sig"))
    if configuration.get("model", {}).get("type") != "qwen3":
        raise RuntimeError(f"Expected a Qwen3 ONNX Runtime GenAI package: {target}")
    ensure_model_link(args.model_directory_link, target)
    print(f"{MODEL_NAME} is ready ({args.provider}): {target}", flush=True)


if __name__ == "__main__":
    main()
