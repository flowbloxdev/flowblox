using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Components.Knowledge;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.PopUp.Provider;
using FlowBlox.UICore.PopUp.Resources;
using FlowBlox.UICore.PopUp.Utilities;
using FlowBlox.UICore.Resources.ClassResources;

namespace FlowBlox.UICore.PopUp.Implementations;

public sealed class VectorStorePopUpProvider : ComponentPopupProviderBase<VectorStore>, IOptionsRegistration
{
    public override string OptionKey => "PopUp.VectorStore.QuickStartCompleted";
    protected override string WindowTitle => Text("Title");

    protected override IReadOnlyList<ComponentPopupItem> CreateItems(VectorStore target) =>
    [
        new ComponentPopupItem(Text("LocalTitle"), Text("LocalDescription"),
            PopUpImageResourceHelper.GetImageSource(VectorStorePopUpImages.ResourceManager, "VectorStoreQuickStart_1")),
        new ComponentPopupItem(Text("RemoteTitle"), Text("RemoteDescription"),
            PopUpImageResourceHelper.GetImageSource(VectorStorePopUpImages.ResourceManager, "VectorStoreQuickStart_2")),
        new ComponentPopupItem(Text("EmbeddingTitle"), Text("EmbeddingDescription"),
            PopUpImageResourceHelper.GetImageSource(VectorStorePopUpImages.ResourceManager, "VectorStoreQuickStart_3"))
    ];

    public void OptionsInit(List<OptionElement> defaults, List<OptionElement> currentOptions)
    {
        defaults.Add(new OptionElement(
            OptionKey,
            bool.FalseString,
            "Tracks completion of the vector-store quick start.",
            OptionElement.OptionType.Boolean));
    }

    private static string Text(string key) => FlowBloxResourceUtil.GetLocalizedString(key, typeof(VectorStoreQuickStartTexts));
}
