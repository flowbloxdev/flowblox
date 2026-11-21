using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;

/// <summary>
/// Describes a project's intended use independently of whether other projects call it.
/// </summary>
[JsonConverter(typeof(FbProjectClassificationConverter))]
public enum FbProjectClassification
{
    [Display(Name = "FbProjectClassification_Solution", ResourceType = typeof(FlowBloxTexts))]
    Solution = 0,

    [Display(Name = "FbProjectClassification_Function", ResourceType = typeof(FlowBloxTexts))]
    Function = 1
}
