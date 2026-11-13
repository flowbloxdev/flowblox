using FlowBlox.Core.Attributes;
using FlowBlox.Core.Provider.Registry;
using global::FlowBlox.Core.Interfaces;
using global::FlowBlox.Core.Models.Components;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Enums;
using System.Collections.ObjectModel;
using FlowBlox.Core.Extensions;

namespace FlowBlox.Core.Models.ObjectManager
{
    public class UserFieldInputItemFactory : IItemFactory<FieldElement>
    {
        public FieldElement Create()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var fieldElement = registry.CreateUserField(UserFieldTypes.Input);
            return fieldElement;
        }
    }

    public class UserFieldMemoryItemFactory : IItemFactory<FieldElement>
    {
        public FieldElement Create()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var fieldElement = registry.CreateUserField(UserFieldTypes.Memory);
            return fieldElement;
        }
    }

    [Display(Name = "UserFieldObjectManager_DisplayName", Description = "UserFieldObjectManager_Description", ResourceType = typeof(FlowBloxTexts))]
    [FlowBloxUIGroup(Name = "UserFieldObjectManager_InputFields", ControlAlignment = ControlAlignment.Fill)]
    [FlowBloxUIGroup(Name = "UserFieldObjectManager_MemoryFields", ControlAlignment = ControlAlignment.Fill)]
    public class UserFieldObjectManager : IObjectManager
    {
        private readonly FlowBloxRegistry _registry;

        public UserFieldObjectManager() : this(null)
        {

        }

        public UserFieldObjectManager(FlowBloxRegistry registry)
        {
            _registry = registry;
            InputFields = new ObservableCollection<FieldElement>();
            MemoryFields = new ObservableCollection<FieldElement>();
            Reload();
        }

        public void Reload()
        {
            InputFields.Clear();
            if (_registry != null)
                InputFields.AddRange(_registry.GetUserFields(UserFieldTypes.Input));

            MemoryFields.Clear();
            if (_registry != null)
                MemoryFields.AddRange(_registry.GetUserFields(UserFieldTypes.Memory));
        }

        [Display(Name = "UserFieldObjectManager_InputFields", GroupName = "UserFieldObjectManager_InputFields", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(DisplayLabel = false, Maximize = true)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(FieldElement.Name) }, SplitMode = true)]
        [FlowBloxAssociation(
            Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete,
            ItemFactory = typeof(UserFieldInputItemFactory))]
        public ObservableCollection<FieldElement> InputFields { get; set; }

        [Display(Name = "UserFieldObjectManager_MemoryFields", GroupName = "UserFieldObjectManager_MemoryFields", ResourceType = typeof(FlowBloxTexts))]
        [FlowBloxUI(DisplayLabel = false, Maximize = true)]
        [FlowBloxListView(ColumnMemberNames = new[] { nameof(FieldElement.Name) }, SplitMode = true)]
        [FlowBloxAssociation(
            Operations = UIOperations.Create | UIOperations.Edit | UIOperations.Delete,
            ItemFactory = typeof(UserFieldMemoryItemFactory))]
        public ObservableCollection<FieldElement> MemoryFields { get; set; }
    }
}
