using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.ShellExecution;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FlowBlox.Core.Models.FlowBlocks.ShellExecution
{
    [Display(Name = "ShellExecuteFlowBlock_DisplayName", Description = "ShellExecuteFlowBlock_Description", ResourceType = typeof(FlowBloxTexts))]
    public class ShellExecuteFlowBlock : BaseResultFlowBlock
    {
        [Required]
        [Display(Name = "ShellExecuteFlowBlock_Command", Description = "ShellExecuteFlowBlock_Command_Description", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(ToolboxCategory = nameof(FlowBloxToolboxCategory.ShellExecution), UiOptions = UIOptions.EnableFieldSelection)]
        public string Command { get; set; }

        [Display(Name = "ShellExecuteFlowBlock_WorkingDirectory", Description = "ShellExecuteFlowBlock_WorkingDirectory_Description", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection | UIOptions.EnableFolderSelection)]
        public string WorkingDirectory { get; set; }

        [Display(Name = "ShellExecuteFlowBlock_TimeoutMilliseconds", Description = "ShellExecuteFlowBlock_TimeoutMilliseconds_Description", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int? TimeoutMilliseconds { get; set; }

        [Display(Name = "ShellExecuteFlowBlock_FailOnNonZeroExitCode", Description = "ShellExecuteFlowBlock_FailOnNonZeroExitCode_Description", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        public bool FailOnNonZeroExitCode { get; set; } = false;

        [Display(Name = "ShellExecuteFlowBlock_ReportStandardOutput", Description = "ShellExecuteFlowBlock_ReportStandardOutput_Description", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        public bool ReportStandardOutput { get; set; } = false;

        [Display(Name = "ShellExecuteFlowBlock_ResultFields", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(Factory = UIFactory.GridView)]
        public ObservableCollection<ResultFieldByEnumValue<ShellExecuteDestinations>> ResultFields { get; set; } = new();

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_tags, 16, SKColors.DarkSlateBlue);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.code_tags, 32, SKColors.DarkSlateBlue);

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;
        public override FlowBlockCategory GetCategory() => FlowBlockCategory.ShellExecution;

        public override void OnAfterCreate()
        {
            CreateDefaultResultFields();
            base.OnAfterCreate();
        }

        private void CreateDefaultResultFields()
        {
            CreateDestinationResultField(ResultFields, ShellExecuteDestinations.Success, FieldTypes.Boolean);
        }

        public override List<FieldElement> Fields
        {
            get
            {
                return ResultFields
                    .Where(x => x.EnumValue != null)
                    .Select(x => x.ResultField)
                    .ExceptNull()
                    .ToList();
            }
        }

        public override List<string> GetDisplayableProperties()
        {
            var properties = base.GetDisplayableProperties();
            properties.Add(nameof(Command));
                properties.Add(nameof(WorkingDirectory));
                properties.Add(nameof(TimeoutMilliseconds));
                properties.Add(nameof(FailOnNonZeroExitCode));
                properties.Add(nameof(ReportStandardOutput));
                return properties;
            }

        public override bool Execute(BaseRuntime runtime, object data)
        {
            return Invoke(runtime, data, () =>
            {

                if (!ResultFields.Any())
                    throw new InvalidOperationException("No result fields have been configured.");

                var command = this.Resolve(owner => owner.Command, required: true);

                var workingDirectory = this.Resolve(owner => owner.WorkingDirectory);

                var executionResult = FlowBloxShellExecutor.Execute(new FlowBloxShellExecutionRequest
                {
                    Command = command,
                    WorkingDirectory = workingDirectory,
                    TimeoutMilliseconds = TimeoutMilliseconds,
                    CancellationToken = runtime.GetCancellationToken(),
                    OnStandardOutputLine = line =>
                    {
                        if (ReportStandardOutput && !string.IsNullOrWhiteSpace(line))
                            runtime.Report($"[Shell StdOut] {line}");
                    },
                    OnStandardErrorLine = line =>
                    {
                        if (ReportStandardOutput && !string.IsNullOrWhiteSpace(line))
                            runtime.Report($"[Shell StdErr] {line}", FlowBloxLogLevel.Warning);
                    }
                });

                var resultRow = new ResultFieldByEnumValueResultBuilder<ShellExecuteDestinations>()
                    .For(ShellExecuteDestinations.Command, command)
                    .For(ShellExecuteDestinations.Success, executionResult.Success.ToString(CultureInfo.InvariantCulture))
                    .For(ShellExecuteDestinations.ExitCode, executionResult.ExitCode.ToString(CultureInfo.InvariantCulture))
                    .For(ShellExecuteDestinations.StandardOutput, executionResult.StandardOutput)
                    .For(ShellExecuteDestinations.StandardError, executionResult.StandardError)
                    .Build(ResultFields);

                GenerateResult(runtime, [resultRow]);

                if (executionResult.WasCancelled)
                    throw new RuntimeCancellationException();

                if (executionResult.TimedOut)
                    throw new InvalidOperationException($"Command execution timed out after {TimeoutMilliseconds} ms.");

                if (!string.IsNullOrWhiteSpace(executionResult.ExceptionMessage))
                    throw new InvalidOperationException(executionResult.ExceptionMessage);

                if (FailOnNonZeroExitCode && executionResult.ExitCode != 0)
                    throw new InvalidOperationException($"Command failed with exit code {executionResult.ExitCode}.");
            });
        }
    }
}
