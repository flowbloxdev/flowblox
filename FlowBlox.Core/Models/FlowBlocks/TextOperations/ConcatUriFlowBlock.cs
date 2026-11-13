using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using SkiaSharp;

namespace FlowBlox.Core.Models.FlowBlocks.TextOperations
{
    [Display(Name = "ConcatUriFlowBlock_DisplayName", 
             Description = "ConcatUriFlowBlock_Description", 
             ResourceType = typeof(FlowBloxTexts))]
    public sealed class ConcatUriFlowBlock : BaseSingleResultFlowBlock
    {
        public ConcatUriFlowBlock()
        {
            UriParts = new ObservableCollection<UriPartDefinition>();
        }

        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.link, 16, SKColors.DarkViolet);
        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.link, 32, SKColors.DarkViolet);

        [Display(Name = "ConcatUriFlowBlock_UriParts", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxDataGrid(IsMovable = true)]
        public ObservableCollection<UriPartDefinition> UriParts { get; set; }

        public override FlowBlockCardinalities GetInputCardinality() => FlowBlockCardinalities.Many;

        public override FlowBlockCategory GetCategory() => FlowBlockCategory.TextOperations;

        public override List<string> GetDisplayableProperties()
        {
            var list = base.GetDisplayableProperties();
            list.Add(nameof(UriParts));
            return list;
        }

        public override bool Execute(Runtime.BaseRuntime runtime, object data)
        {
            return base.Invoke(runtime, data, () =>
            {

                var parts = UriParts?
                    .Select(p => p?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                if (parts!.Count == 0)
                {
                    GenerateResult(runtime, string.Empty);
                    return;
                }

                Uri current = null;
                foreach (var part in parts)
                {
                    current = ResolveUrl(current, FlowBloxFieldHelper.ReplaceFieldsInString(part));
                }

                var final = current?.AbsoluteUri ?? string.Empty;
                GenerateResult(runtime, final);
            });
        }

        private static Uri ResolveUrl(Uri current, string nextPart)
        {
            if (string.IsNullOrWhiteSpace(nextPart))
                return current;

            if (Uri.TryCreate(nextPart, UriKind.Absolute, out var abs))
                return abs;

            if (nextPart.StartsWith("//"))
            {
                if (current != null)
                    return new Uri($"{current.Scheme}:{nextPart}", UriKind.Absolute);

                return new Uri($"https:{nextPart}", UriKind.Absolute);
            }

            if (current == null)
                throw new InvalidOperationException($"Relative URI '{nextPart}' cannot be resolved without a base URI.");

            return new Uri(current, nextPart);
        }
    }
}
