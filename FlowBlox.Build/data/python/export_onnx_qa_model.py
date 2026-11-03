"""Download and export a Hugging Face extractive-QA checkpoint to ONNX."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path


DEFAULT_MODEL_ID = "timpal0l/mdeberta-v3-base-squad2"
DEFAULT_MODEL_ROOT = Path.home() / ".flowblox" / "onnx" / "qa"
SENTENCEPIECE_ASSETS = {
    DEFAULT_MODEL_ID: ("microsoft/mdeberta-v3-base", "spm.model"),
    "deepset/deberta-v3-base-squad2": (
        "microsoft/deberta-v3-base",
        "spm.model",
    ),
    "deepset/xlm-roberta-base-squad2": (
        "FacebookAI/xlm-roberta-base",
        "sentencepiece.bpe.model",
    ),
}
TOKENIZER_SOURCES = {
    model_id: repository
    for model_id, (repository, _) in SENTENCEPIECE_ASSETS.items()
}
TOKENIZER_SOURCES.update({
    "deepset/gelectra-base-germanquad": "deepset/gelectra-base",
})
REQUIRED_MODEL_FILES = ("model.onnx", "config.json")
REQUIRED_TOKENIZER_METADATA_FILES = ("tokenizer_config.json",)


def dependencies():
    try:
        from huggingface_hub import hf_hub_download
        from optimum.exporters.onnx import main_export
        from transformers import AutoTokenizer
    except ImportError:
        print("Installing Optimum ONNX and its ONNX Runtime dependencies...", flush=True)
        subprocess.check_call(
            [
                sys.executable,
                "-m",
                "pip",
                "install",
                "--upgrade",
                "optimum-onnx[onnxruntime]",
            ]
        )
        from huggingface_hub import hf_hub_download
        from optimum.exporters.onnx import main_export
        from transformers import AutoTokenizer

    return main_export, hf_hub_download, AutoTokenizer


def parse_args():
    parser = argparse.ArgumentParser(
        description="Export an extractive question-answering model for FlowBlox."
    )
    parser.add_argument(
        "--model-id",
        default=DEFAULT_MODEL_ID,
        help=f"Hugging Face model ID (default: {DEFAULT_MODEL_ID}).",
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
            "Root directory below which the model-name folder is stored "
            f"(default: {DEFAULT_MODEL_ROOT})."
        ),
    )
    parser.add_argument(
        "--sentencepiece-source",
        help="Optional repository from which spm.model must be copied after export.",
    )
    parser.add_argument(
        "--sentencepiece-filename",
        help="SentencePiece asset name. Defaults to the known model asset or spm.model.",
    )
    return parser.parse_args()


def has_supported_tokenizer(target: Path) -> bool:
    single_file_tokenizers = (
        "vocab.txt",
        "spm.model",
        "sentencepiece.bpe.model",
        "tokenizer.model",
    )
    return any((target / name).is_file() for name in single_file_tokenizers) or (
        (target / "vocab.json").is_file() and (target / "merges.txt").is_file()
    )


def is_complete_model(target: Path) -> bool:
    return (
        has_exported_model(target)
        and has_tokenizer_metadata(target)
        and has_supported_tokenizer(target)
    )


def has_exported_model(target: Path) -> bool:
    return all((target / name).is_file() for name in REQUIRED_MODEL_FILES)


def has_tokenizer_metadata(target: Path) -> bool:
    return all(
        (target / name).is_file() for name in REQUIRED_TOKENIZER_METADATA_FILES
    )


def main() -> None:
    args = parse_args()
    model_name = args.model_id.rstrip("/").rsplit("/", 1)[-1]
    target = args.output_directory or (args.model_root_directory / model_name)
    target = target.expanduser().resolve()
    if is_complete_model(target):
        print(f"ONNX QA model is already available in: {target}", flush=True)
        return

    target.mkdir(parents=True, exist_ok=True)

    known_sentencepiece_asset = SENTENCEPIECE_ASSETS.get(args.model_id)
    sentencepiece_source = args.sentencepiece_source or (
        known_sentencepiece_asset[0] if known_sentencepiece_asset else None
    )
    sentencepiece_filename = args.sentencepiece_filename or (
        known_sentencepiece_asset[1]
        if known_sentencepiece_asset
        else "spm.model"
    )
    main_export, hf_hub_download, auto_tokenizer = dependencies()
    if not has_exported_model(target):
        print(f"Exporting {args.model_id} to {target}...", flush=True)
        main_export(
            model_name_or_path=args.model_id,
            output=target,
            task="question-answering",
        )
    else:
        print(f"Using existing ONNX model in: {target}", flush=True)

    tokenizer_source = (
        args.sentencepiece_source
        or TOKENIZER_SOURCES.get(args.model_id, args.model_id)
    )
    if not has_tokenizer_metadata(target) or not has_supported_tokenizer(target):
        print(f"Saving tokenizer assets from {tokenizer_source}...", flush=True)
        tokenizer = auto_tokenizer.from_pretrained(tokenizer_source)
        tokenizer.save_pretrained(target)

    sentencepiece_target = target / sentencepiece_filename
    if sentencepiece_source and not sentencepiece_target.is_file():
        source = hf_hub_download(
            repo_id=sentencepiece_source,
            filename=sentencepiece_filename,
        )
        shutil.copy2(source, sentencepiece_target)

    required_files = REQUIRED_MODEL_FILES + REQUIRED_TOKENIZER_METADATA_FILES
    missing = [name for name in required_files if not (target / name).is_file()]
    if missing:
        raise RuntimeError(f"Export incomplete; missing: {', '.join(missing)}")
    if not has_supported_tokenizer(target):
        raise RuntimeError(
            "Export incomplete; no supported WordPiece, byte-level BPE or "
            "SentencePiece tokenizer assets found."
        )

    print(f"ONNX QA model ready: {target}", flush=True)
    print("Select this directory as ModelFolder in the ONNX QA FlowBlock.", flush=True)


if __name__ == "__main__":
    main()
