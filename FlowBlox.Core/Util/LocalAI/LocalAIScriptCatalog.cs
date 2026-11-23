using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.FlowBlocks.AI;
using FlowBlox.Core.Models.FlowBlocks.Base;

namespace FlowBlox.Core.Util.LocalAI;

/// <summary>
/// Keeps bundled script metadata shared by the setup dialog and project configuration.
/// </summary>
public static class LocalAIScriptCatalog
{
    public static IReadOnlyList<LocalAIScriptMetadata> Scripts { get; } = Array.AsReadOnly(new[]
    {
        new LocalAIScriptMetadata
        {
            Model = LocalAIModel.Qwen3Instruct,
            ScriptName = "download_qwen3_instruct_onnx.py",
            ModelFolder = "$Project::InputDirectory/local-ai/qwen3",
            ChatTemplate = "<|im_start|>system\n{SystemPrompt}<|im_end|>\n<|im_start|>user\n{UserPrompt}<|im_end|>\n<|im_start|>assistant\n"
        },
        new LocalAIScriptMetadata
        {
            Model = LocalAIModel.Phi4Mini,
            ScriptName = "download_phi4_mini_instruct_onnx.py",
            ModelFolder = "$Project::InputDirectory/local-ai/phi4",
            ChatTemplate = "<|system|>{SystemPrompt}<|end|><|user|>{UserPrompt}<|end|><|assistant|>"
        },
        new LocalAIScriptMetadata
        {
            Model = LocalAIModel.ExtractiveQA,
            ScriptName = "export_onnx_qa_model.py",
            ModelFolder = $"$Options::{OptionsConstants.AIOnnxQAModelRootDirectory}/{OnnxQAFlowBlock.DefaultModelFolderName}"
        }
    });

    public static IEnumerable<LocalAIScriptMetadata> ForBlock(BaseFlowBlock block) =>
        Scripts.Where(script => Supports(script, block));

    public static bool Supports(LocalAIScriptMetadata script, BaseFlowBlock block) =>
        script.IsGenAI ? block is OnnxGenAIFlowBlock : block is OnnxQAFlowBlock;
}
