using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Actions;
using FlowBlox.Core.Models.Components.IO;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.Logic;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DeepCopier;
using FlowBlox.UICore.Manager;
using FlowBloxTest.Services;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ComponentAssistantScopeTests
{
    private FlowBloxProject _previousProject;
    private FlowBloxProject _project;
    private PropertyViewTransactionManager _transaction;
    private RegexSelectorFlowBlock _source;
    private RegexSelectorFlowBlock _working;
    private DefaultToolApi _api;
    private string _previousProtocolDirectory;

    [TestInitialize]
    public void Initialize()
    {
        var options = FlowBloxOptions.GetOptionInstance();
        options.InitDefaults(false);
        var protocolOption = options.GetOption(OptionsConstants.PathsDeepCopierProtocolDir);
        _previousProtocolDirectory = protocolOption.Value;
        protocolOption.Value = Path.Combine(Path.GetTempPath(), "FlowBloxComponentScopeTests");
        _previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        _project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = _project;
        _source = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<RegexSelectorFlowBlock>();
        _project.FlowBloxRegistry.PostProcessFlowBlockCreated(_source);
        _project.FlowBloxRegistry.Register(_source);
        _source.RegularExpression = "Before";
        var start = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<FlowBlox.Core.Models.FlowBlocks.SequenceFlow.StartFlowBlock>();
        _project.FlowBloxRegistry.Register(start);
        start.ReferencedFlowBlocks.Add(_source);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _transaction?.Cancel();
        FlowBloxProjectManager.Instance.ActiveProject = _previousProject;
        FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsDeepCopierProtocolDir).Value = _previousProtocolDirectory;
    }

    private T Open<T>(T source) where T : BaseFlowBlock
    {
        _transaction = new PropertyViewTransactionManager();
        var opened = _transaction.Open(source);
        var working = (T)opened.TransientTarget;
        _api = new DefaultToolApi
        {
            ComponentContext = new AssistantComponentContext(
                working,
                opened.Registry,
                () => FlowBloxRegistryProvider.IsTransactionActive(opened.Registry),
                _transaction.Append)
        };
        return working;
    }

    private Task<ToolResponse> Execute(string name, JObject arguments) =>
        _api.ExecuteAsync(new ToolRequest { ToolName = name, Arguments = arguments }, CancellationToken.None);

    [TestMethod]
    public async Task DetachedEditorAboveComponentTransaction_DoesNotInvalidateOrRedirectAssistantWrites()
    {
        _working = Open(_source);
        var componentRegistry = _api.ComponentContext.Registry;
        var detachedTransaction = new PropertyViewTransactionManager();
        var detached = detachedTransaction.Open(_working, detached: true);
        var detachedCopy = (RegexSelectorFlowBlock)detached.TransientTarget;
        detachedCopy.RegularExpression = "Detached";
        try
        {
            Assert.IsFalse(FlowBloxRegistryProvider.IsCurrentTransaction(componentRegistry));
            var response = await Task.Run(() => Execute("UpdateFlowBlock", new JObject
            {
                ["name"] = _working.Name,
                ["path"] = "/RegularExpression",
                ["value"] = "Assistant"
            }));

            Assert.IsTrue(response.Ok, response.Error);
            Assert.AreEqual("Assistant", _working.RegularExpression);
            Assert.AreEqual("Detached", detachedCopy.RegularExpression);
            Assert.AreEqual("Before", _source.RegularExpression);
            Assert.AreSame(detached.Registry, FlowBloxRegistryProvider.GetRegistry());
        }
        finally
        {
            detachedTransaction.Cancel();
        }

        _api.ComponentContext.EnsureActive();
        _transaction.Commit(_source, _working);
        Assert.AreEqual("Assistant", _source.RegularExpression);
        Assert.ThrowsExactly<InvalidOperationException>(() => _api.ComponentContext.EnsureActive());
    }

    [TestMethod]
    public void ToolCatalogFiltersProjectHandlersAndPreservesOwnedObjectAndSkillTools()
    {
        var projectTools = new DefaultToolApi().GetToolDefinitions().Select(tool => tool.Name).ToArray();
        CollectionAssert.Contains(projectTools, "RunProjectDebugTest");
        CollectionAssert.Contains(projectTools, "GetPSProjects");
        _working = Open(_source);
        var componentTools = _api.GetToolDefinitions().Select(tool => tool.Name).ToArray();
        foreach (var excluded in new[] { "RunProjectDebugTest", "GetLastDebugArtefact", "InspectFieldValue",
            "RunTestDefinition", "GetPSProjects", "SkillProjectEditing", "CreateOrUpdateSkillProject", "CreateFlowBlock" })
        {
            CollectionAssert.DoesNotContain(componentTools, excluded);
        }
        foreach (var included in new[] { "CreateManagedObject", "CreateField", "UpdateManagedObject", "GetSkillProjects",
            "GetSkillProject", "GetDistributedDataContent", "ExecuteExternalProject", "InspectOutputFieldValue",
            "RunGenerationStrategies", "ConnectFlowBlocks" })
        {
            CollectionAssert.Contains(componentTools, included);
        }
    }

    [TestMethod]
    [DataRow("RunProjectDebugTest")]
    [DataRow("GetLastDebugArtefact")]
    [DataRow("GetPSProjects")]
    [DataRow("CreateOrUpdateSkillProject")]
    [DataRow("SkillProjectEditing")]
    public async Task HiddenProjectToolIsAlsoRejectedWhenCalledDirectly(string name)
    {
        _working = Open(_source);
        var response = await Execute(name, new JObject());
        Assert.IsFalse(response.Ok);
        StringAssert.Contains(response.Error, "unavailable in component mode");
    }

    [TestMethod]
    public async Task ExplanationScopeFiltersInitialContentCatalogAndExplicitLoads()
    {
        Assert.IsFalse(AssistantPromptCatalog.TryGetEntry(AssistantPromptCatalog.ComponentScopeKey, out _));
        Assert.IsTrue(AssistantPromptCatalog.TryGetEntry(AssistantPromptCatalog.DebuggingKey, out _));
        _working = Open(_source);
        using (ToolHandlerUtilities.BeginComponentScope(_api.ComponentContext))
        {
            var entries = AssistantPromptCatalog.GetAllEntries();
            Assert.IsFalse(entries.Any(entry => entry.Key == AssistantPromptCatalog.DebuggingKey));
            Assert.IsFalse(entries.Any(entry => entry.Key == AssistantPromptCatalog.SpecialProjectSpaceKey));
            Assert.IsTrue(entries.Single(entry => entry.Key == AssistantPromptCatalog.ComponentScopeKey).IsIncludedInInitialPrompt);
            Assert.IsTrue(entries.Any(entry => entry.Key == AssistantPromptCatalog.IterationContextKey));
            Assert.IsTrue(entries.Any(entry => entry.Key == AssistantPromptCatalog.SpecialGeneratorsKey));
            var bootstrap = AssistantPromptBuilder.BuildSessionBootstrapPrompt(_api.GetToolDefinitions());
            StringAssert.Contains(bootstrap, "# Component editor scope");
            Assert.IsFalse(bootstrap.Contains("Topic: Explaining Debugging", StringComparison.Ordinal));
            Assert.IsFalse(bootstrap.Contains(AssistantPromptCatalog.SpecialProjectSpaceKey, StringComparison.Ordinal));
        }
        var blocked = await Execute("LoadExplanation", new JObject { ["key"] = AssistantPromptCatalog.DebuggingKey });
        Assert.IsFalse(blocked.Ok);
        var initial = await Execute("LoadExplanation", new JObject { ["key"] = AssistantPromptCatalog.ComponentScopeKey });
        Assert.IsTrue(initial.Ok, initial.Error);
        Assert.IsFalse(initial.Result.Value<bool>("loaded"));
    }

    [TestMethod]
    [DataRow(false, AssistantPromptCatalog.SpecialSkillsKey, true)]
    [DataRow(true, AssistantPromptCatalog.SpecialSkillsKey, true)]
    [DataRow(false, AssistantPromptCatalog.SpecialSkillProjectEditingKey, true)]
    [DataRow(true, AssistantPromptCatalog.SpecialSkillProjectEditingKey, false)]
    public async Task SkillExplanationLoadsRespectUsageAndAuthoringScopes(bool componentMode, string key, bool allowed)
    {
        if (componentMode)
            _working = Open(_source);
        else
            _api = new DefaultToolApi();

        var response = await Execute("LoadExplanation", new JObject { ["key"] = key });
        Assert.AreEqual(allowed, response.Ok, response.Error);
        if (allowed)
        {
            var retained = response.RetainedContents.Single();
            Assert.AreEqual("explanation:" + key, retained.Key);
            Assert.IsFalse(string.IsNullOrWhiteSpace(retained.Content));
        }
        else
        {
            Assert.AreEqual(0, response.RetainedContents.Count);
            CollectionAssert.DoesNotContain(response.Result["availableKeys"]!.Values<string>().ToArray(), key);
        }
    }

    [TestMethod]
    public async Task IncomingConnectionsChangeOnlyWorkingCopyAndOutgoingConnectionsAreRejected()
    {
        var predecessor = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<RegexSelectorFlowBlock>();
        _project.FlowBloxRegistry.Register(predecessor);
        _working = Open(_source);
        var response = await Execute("ConnectFlowBlocks", new JObject
        {
            ["from"] = predecessor.Name,
            ["to"] = _working.Name
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreSame(predecessor, _working.ReferencedFlowBlocks.Single());
        Assert.AreEqual(0, _source.ReferencedFlowBlocks.Count);
        response = await Execute("ConnectFlowBlocks", new JObject
        {
            ["from"] = _working.Name,
            ["to"] = predecessor.Name
        });
        Assert.IsFalse(response.Ok);
        Assert.AreEqual(0, predecessor.ReferencedFlowBlocks.Count);
        response = await Execute("DisconnectFlowBlocks", new JObject
        {
            ["from"] = predecessor.Name,
            ["to"] = _working.Name
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual(0, _working.ReferencedFlowBlocks.Count);
    }

    [TestMethod]
    public async Task UpdatesWorkingCopyAndCancelLeavesOriginalUntouched()
    {
        _working = Open(_source);
        var response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = _working.Name,
            ["path"] = "/RegularExpression",
            ["value"] = "After"
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual("After", _working.RegularExpression);
        Assert.AreEqual("Before", _source.RegularExpression);
        _transaction.Cancel();
        Assert.AreEqual("Before", _source.RegularExpression);
        response = await Execute("UpdateFlowBlock", new JObject { ["name"] = _working.Name, ["path"] = "/Group", ["value"] = "late" });
        Assert.IsFalse(response.Ok);
    }

    [TestMethod]
    public async Task BatchAllowsIncomingReferenceButCannotModifyForeignComponentOrCreateUserField()
    {
        var other = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<RegexSelectorFlowBlock>();
        _project.FlowBloxRegistry.Register(other);
        _working = Open(_source);
        var response = await Execute("BatchExecuteToolRequests", new JObject
        {
            ["continueOnError"] = true,
            ["requests"] = new JArray
            {
                new JObject { ["toolName"] = "UpdateFlowBlock", ["arguments"] = new JObject
                    { ["name"] = other.Name, ["path"] = "/RegularExpression", ["value"] = "foreign" } },
                new JObject { ["toolName"] = "UpdateFlowBlock", ["arguments"] = new JObject
                    { ["name"] = _working.Name, ["path"] = "/ReferencedFlowBlocks/Link", ["value"] = other.Name } },
                new JObject { ["toolName"] = "CreateUserField", ["arguments"] = new JObject
                    { ["name"] = "Unowned" } }
            }
        });
        Assert.IsFalse(response.Ok);
        Assert.IsNull(other.RegularExpression);
        Assert.AreEqual(1, _working.ReferencedFlowBlocks.Count);
        Assert.AreSame(other, _working.ReferencedFlowBlocks.Single());
        Assert.AreEqual(0, _source.ReferencedFlowBlocks.Count);
        Assert.IsFalse(_api.ComponentContext.Registry.GetManagedObjects().Any(item => item.Name == "Unowned"));
    }

    [TestMethod]
    public async Task OwnedResultFieldCanBeEditedWithoutChangingOriginal()
    {
        _working = Open(_source);
        var response = await Execute("UpdateManagedObject", new JObject
        {
            ["name"] = _working.ResultField.FullyQualifiedName,
            ["path"] = "/Name",
            ["value"] = "BookTitle"
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual("BookTitle", _working.ResultField.Name);
        Assert.AreNotEqual("BookTitle", _source.ResultField.Name);
        response = await Execute("UpdateManagedObject", new JObject
        {
            ["name"] = _working.ResultField.FullyQualifiedName,
            ["path"] = "/Source/Unlink"
        });
        Assert.IsFalse(response.Ok);
        Assert.AreSame(_working, _working.ResultField.Source);
    }

    [TestMethod]
    public async Task MissingSingleResultFieldIsCreatedLocallyAndCommittedWithOwner()
    {
        _source.ResultField = null;
        _working = Open(_source);
        var response = await Execute("CreateField", new JObject { ["sourceFlowBlockName"] = _working.Name, ["overrideName"] = "Title" });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.IsNull(_source.ResultField);
        Assert.AreSame(_working, _working.ResultField.Source);
        response = await Execute("CreateField", new JObject { ["sourceFlowBlockName"] = _working.Name });
        Assert.IsFalse(response.Ok);
        _transaction.Commit(_source, _working);
        Assert.AreEqual("Title", _source.ResultField.Name);
        Assert.AreSame(_source, _source.ResultField.Source);
        Assert.IsTrue(_project.FlowBloxRegistry.GetFieldElements().Any(field => ReferenceEquals(field, _source.ResultField)));
    }

    [TestMethod]
    public async Task MultiResultFieldIsAttachedToDefiningCollection()
    {
        var source = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<FieldCopyFlowBlock>();
        _project.FlowBloxRegistry.Register(source);
        var working = Open(source);
        var response = await Execute("CreateField", new JObject
        {
            ["sourceFlowBlockName"] = working.Name,
            ["overrideName"] = "Title"
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual(0, working.Fields.Count);
        Assert.IsNotNull(response.Result["componentScopeWarning"]);
        response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = working.Name,
            ["path"] = "/CopiedFields/Link",
            ["value"] = new JObject { ["resolveFieldElementByFQName"] = response.Result.Value<string>("fullyQualifiedFieldName") }
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.IsNull(response.Result["componentScopeWarning"]);
        Assert.AreEqual(1, working.Fields.Count);
        Assert.AreSame(working, working.Fields.Single().Source);
        Assert.AreEqual(0, source.Fields.Count);
        _transaction.Cancel();
        Assert.AreEqual(0, source.Fields.Count);
        Assert.IsFalse(_project.FlowBloxRegistry.GetFieldElements().Any(field => field.Source == working));
    }

    [TestMethod]
    public async Task OwnedTestDefinitionCanBeCreatedAndEditedLocally()
    {
        _working = Open(_source);
        var response = await Execute("CreateManagedObject", new JObject
        {
            ["typeFullName"] = typeof(FlowBloxTestDefinition).FullName,
            ["name"] = "LocalTest"
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual(0, _working.TestDefinitions.Count);
        Assert.IsNotNull(response.Result["componentScopeWarning"]);
        Assert.AreEqual(0, _source.TestDefinitions.Count);
        response = await Execute("UpdateManagedObject", new JObject
        {
            ["name"] = "LocalTest",
            ["path"] = "/Name",
            ["value"] = "RenamedTest"
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.IsNotNull(response.Result["componentScopeWarning"]);
        response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = _working.Name,
            ["path"] = "/TestDefinitions/Link",
            ["value"] = new JObject { ["resolveManagedObjectByName"] = "RenamedTest" }
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.IsNull(response.Result["componentScopeWarning"]);
        Assert.AreEqual("RenamedTest", _working.TestDefinitions.Single().Name);
    }

    [TestMethod]
    public async Task LinkingBackReferenceCreatesAnEditableWorkingCopy()
    {
        var external = new FlowBloxTestDefinition { Name = "ExistingTest" };
        _project.FlowBloxRegistry.RegisterManagedObject(external);
        _working = Open(_source);
        var response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = _working.Name,
            ["path"] = "/TestDefinitions/Link",
            ["value"] = new JObject { ["resolveManagedObjectByName"] = external.Name }
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreNotSame(external, _working.TestDefinitions.Single());
        response = await Execute("UpdateManagedObject", new JObject
        {
            ["name"] = external.Name,
            ["path"] = "/Name",
            ["value"] = "LocalName"
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreEqual("ExistingTest", external.Name);
        Assert.AreEqual("LocalName", _working.TestDefinitions.Single().Name);
        _transaction.Cancel();
        Assert.AreEqual(0, _source.TestDefinitions.Count);
    }

    [TestMethod]
    public async Task BatchWarningsReflectCompletedOwnerLinks()
    {
        _working = Open(_source);
        var response = await Execute("BatchExecuteToolRequests", new JObject
        {
            ["requests"] = new JArray
            {
                new JObject
                {
                    ["toolName"] = "CreateManagedObject",
                    ["arguments"] = new JObject
                    {
                        ["typeFullName"] = typeof(FlowBloxTestDefinition).FullName,
                        ["name"] = "BatchTest"
                    }
                },
                new JObject
                {
                    ["toolName"] = "UpdateFlowBlock",
                    ["arguments"] = new JObject
                    {
                        ["name"] = _working.Name,
                        ["path"] = "/TestDefinitions/Link",
                        ["value"] = new JObject { ["resolveManagedObjectByName"] = "BatchTest" }
                    }
                }
            }
        });
        Assert.IsTrue(response.Ok, response.Error);
        response.RefreshForSerialization();
        var results = (JArray)response.Result["batchResults"];
        Assert.IsTrue(results.All(result => result["result"]?["componentScopeWarning"] == null));
        Assert.AreEqual("BatchTest", _working.TestDefinitions.Single().Name);
    }

    [TestMethod]
    public async Task UnlinkedNewObjectWarningSurvivesBatchAndCancelDiscardsObject()
    {
        _working = Open(_source);
        var response = await Execute("BatchExecuteToolRequests", new JObject
        {
            ["requests"] = new JArray
            {
                new JObject
                {
                    ["toolName"] = "CreateManagedObject",
                    ["arguments"] = new JObject
                    {
                        ["typeFullName"] = typeof(FlowBloxTestDefinition).FullName,
                        ["name"] = "PendingTest"
                    }
                }
            }
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.IsNotNull(response.Result["batchResults"]?[0]?["result"]?["componentScopeWarning"]);
        Assert.IsTrue(_api.ComponentContext.Registry.GetManagedObjects().Any(item => item.Name == "PendingTest"));
        Assert.IsFalse(_project.FlowBloxRegistry.GetManagedObjects().Any(item => item.Name == "PendingTest"));
        _transaction.Cancel();
        Assert.IsFalse(_project.FlowBloxRegistry.GetManagedObjects().Any(item => item.Name == "PendingTest"));
    }

    [TestMethod]
    public async Task CreatedResultFieldRemainsRegisteredAfterCommitAndCopyPaste()
    {
        _source.ResultField = null;
        _working = Open(_source);
        var response = await Execute("CreateField", new JObject
        {
            ["sourceFlowBlockName"] = _working.Name,
            ["overrideName"] = "Title"
        });
        Assert.IsTrue(response.Ok, response.Error);
        _transaction.Commit(_source, _working);

        var pasted = CopyAndPaste(_source);
        Assert.AreNotSame(_source.ResultField, pasted.ResultField);
        Assert.AreSame(pasted, pasted.ResultField.Source);
        Assert.IsTrue(_project.FlowBloxRegistry.GetManagedObjects().Any(item => ReferenceEquals(item, _source.ResultField)));
        Assert.IsTrue(_project.FlowBloxRegistry.GetManagedObjects().Any(item => ReferenceEquals(item, pasted.ResultField)));
    }

    [TestMethod]
    public async Task CreatedTestDefinitionRemainsRegisteredAfterCommitAndCopyPaste()
    {
        _working = Open(_source);
        var response = await Execute("CreateManagedObject", new JObject
        {
            ["typeFullName"] = typeof(FlowBloxTestDefinition).FullName,
            ["name"] = "OwnedTest"
        });
        Assert.IsTrue(response.Ok, response.Error);
        response = await Execute("UpdateManagedObject", new JObject
        {
            ["name"] = "OwnedTest",
            ["updates"] = new JArray
            {
                new JObject
                {
                    ["path"] = "/Entries/0/FlowBlock/Link",
                    ["value"] = new JObject { ["resolveFlowBlockByName"] = _working.Name }
                },
                new JObject { ["path"] = "/Entries/0/Execute", ["value"] = true }
            }
        });
        Assert.IsTrue(response.Ok, response.Error);
        response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = _working.Name,
            ["path"] = "/TestDefinitions/Link",
            ["value"] = new JObject { ["resolveManagedObjectByName"] = "OwnedTest" }
        });
        Assert.IsTrue(response.Ok, response.Error);
        _transaction.Commit(_source, _working);

        var pasted = CopyAndPaste(_source);
        var originalTest = _source.TestDefinitions.Single();
        var pastedTest = pasted.TestDefinitions.Single();
        Assert.AreNotSame(originalTest, pastedTest);
        Assert.IsTrue(_project.FlowBloxRegistry.GetManagedObjects().Any(item => ReferenceEquals(item, originalTest)));
        Assert.IsTrue(_project.FlowBloxRegistry.GetManagedObjects().Any(item => ReferenceEquals(item, pastedTest)));
        Assert.IsTrue(pastedTest.Entries.Any(entry => ReferenceEquals(entry.FlowBlock, pasted)),
            "Original entries: " + string.Join(",", originalTest.Entries.Select(entry => entry.FlowBlock?.Name)) +
            "; pasted entries: " + string.Join(",", pastedTest.Entries.Select(entry => entry.FlowBlock?.Name)));
        Assert.IsFalse(pastedTest.Entries.Any(entry => ReferenceEquals(entry.FlowBlock, _source)));
    }

    [TestMethod]
    public async Task RegularManagedObjectRemainsSharedAndCannotBeCreatedInComponentMode()
    {
        var table = new SQLTable { Name = "SharedTable" };
        _project.FlowBloxRegistry.RegisterManagedObject(table);
        var source = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<TableReaderFlowBlock>();
        _project.FlowBloxRegistry.Register(source);
        var working = Open(source);
        var response = await Execute("CreateManagedObject", new JObject
        {
            ["typeFullName"] = typeof(SQLTable).FullName,
            ["name"] = "AnotherTable"
        });
        Assert.IsFalse(response.Ok);
        Assert.IsFalse(_api.ComponentContext.Registry.GetManagedObjects().Any(item => item.Name == "AnotherTable"));
        response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = working.Name,
            ["path"] = "/ReferencedTable/Link",
            ["value"] = new JObject { ["resolveManagedObjectByName"] = table.Name }
        });
        Assert.IsTrue(response.Ok, response.Error);
        _transaction.Commit(source, working);

        var pasted = CopyAndPaste(source);
        Assert.AreSame(table, source.ReferencedTable);
        Assert.AreSame(table, pasted.ReferencedTable);
        Assert.IsTrue(_project.FlowBloxRegistry.GetManagedObjects().Any(item => ReferenceEquals(item, table)));
    }

    private static T CopyAndPaste<T>(T source) where T : BaseFlowBlock
    {
        var copy = (T)new DynamicDeepCopier(FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(source)).Copy(source);
        copy.Name = "CopyOf" + source.Name;
        var paste = (T)new DynamicDeepCopier(FlowBloxDeepCopyStrategy.Instance.GetDeepCopyActions(copy)).Copy(copy);
        new FlowBloxCreateAction { FlowBlock = paste }.Invoke();
        return paste;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FileWriterRejectsFileObjectCreationBeforeRegistration(bool batch)
    {
        var source = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<FileWriterFlowBlock>();
        _project.FlowBloxRegistry.PostProcessFlowBlockCreated(source);
        _project.FlowBloxRegistry.Register(source);
        var working = Open(source);
        var createArgs = new JObject
        {
            ["typeFullName"] = typeof(FileObject).FullName,
            ["name"] = "ForbiddenFile"
        };
        var response = batch
            ? await Execute("BatchExecuteToolRequests", new JObject
            {
                ["continueOnError"] = true,
                ["requests"] = new JArray
                {
                    new JObject { ["toolName"] = "CreateManagedObject", ["arguments"] = createArgs },
                    new JObject
                    {
                        ["toolName"] = "UpdateFlowBlock",
                        ["arguments"] = new JObject
                        {
                            ["name"] = working.Name,
                            ["path"] = "/DataSource/Link",
                            ["value"] = new JObject { ["resolveManagedObjectByName"] = "ForbiddenFile" }
                        }
                    }
                }
            })
            : await Execute("CreateManagedObject", createArgs);

        Assert.IsFalse(response.Ok);
        Assert.IsNull(working.DataSource);
        Assert.IsFalse(_api.ComponentContext.Registry.GetManagedObjects().OfType<FileObject>().Any());
        _transaction.Commit(source, working);
        Assert.IsNull(source.DataSource);
        Assert.IsFalse(_project.FlowBloxRegistry.GetManagedObjects().OfType<FileObject>().Any());
    }

    [TestMethod]
    public async Task FileWriterCanLinkExistingFileButCannotConfigureOrCreateItInline()
    {
        var file = new FileObject { Name = "ExistingFile" };
        _project.FlowBloxRegistry.RegisterManagedObject(file);
        var source = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<FileWriterFlowBlock>();
        _project.FlowBloxRegistry.PostProcessFlowBlockCreated(source);
        _project.FlowBloxRegistry.Register(source);
        var working = Open(source);
        var response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = working.Name,
            ["path"] = "/DataSource",
            ["value"] = new JObject { ["$type"] = typeof(FileObject).FullName, ["Name"] = "InlineFile" }
        });
        Assert.IsFalse(response.Ok);
        Assert.IsNull(working.DataSource);
        Assert.IsFalse(_api.ComponentContext.Registry.GetManagedObjects().Any(item => item.Name == "InlineFile"));

        response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = working.Name,
            ["path"] = "/DataSource/Link",
            ["value"] = new JObject { ["resolveManagedObjectByName"] = file.Name }
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreSame(file, working.DataSource);
        response = await Execute("UpdateManagedObject", new JObject
        {
            ["name"] = file.Name,
            ["path"] = "/Name",
            ["value"] = "ForbiddenRename"
        });
        Assert.IsFalse(response.Ok);
        Assert.AreEqual("ExistingFile", file.Name);
    }

    [TestMethod]
    public async Task ReferencedForeignFieldCannotReplaceOwnedResultField()
    {
        var foreign = _project.FlowBloxRegistry.CreateFlowBlockUnregistered<RegexSelectorFlowBlock>();
        _project.FlowBloxRegistry.PostProcessFlowBlockCreated(foreign);
        _project.FlowBloxRegistry.Register(foreign);
        _working = Open(_source);
        var original = _working.ResultField;
        var response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = _working.Name,
            ["path"] = "/ResultField/Link",
            ["value"] = new JObject { ["resolveFieldElementByFQName"] = foreign.ResultField.FullyQualifiedName }
        });
        Assert.IsFalse(response.Ok);
        Assert.AreSame(original, _working.ResultField);
    }

    [TestMethod]
    public async Task SnapshotDetectsOwnedFieldConfigurationChanges()
    {
        _working = Open(_source);
        var context = _api.ComponentContext;
        var before = context.CaptureJson();
        var response = await Execute("UpdateManagedObject", new JObject
        {
            ["name"] = _working.ResultField.FullyQualifiedName,
            ["path"] = "/Index",
            ["value"] = 37
        });
        Assert.IsTrue(response.Ok, response.Error);
        Assert.AreNotEqual(before, context.CaptureJson());
    }

    [TestMethod]
    [DataRow("/ReferencedFlowBlocks/Link")]
    [DataRow("ReferencedFlowBlocks.Link")]
    public async Task BothPathFormatsRejectSelfReferences(string path)
    {
        _working = Open(_source);
        var response = await Execute("UpdateFlowBlock", new JObject
        {
            ["name"] = _working.Name,
            ["path"] = path,
            ["value"] = _working.Name
        });
        Assert.IsFalse(response.Ok);
        Assert.AreEqual(0, _working.ReferencedFlowBlocks.Count);
    }

    [TestMethod]
    public void HistoryKeepsProjectAndComponentConversationsSeparate()
    {
        var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIAssistantHistoryDirectory);
        var previous = option.Value;
        option.Value = Path.Combine(Path.GetTempPath(), "FlowBloxComponentHistoryTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = FlowBlox.AIAssistant.History.AiAssistantHistoryStore.Instance;
            using var projectHistory = new FlowBlox.UICore.ViewModels.AiAssistantHistoryViewModel();
            using var componentHistory = new FlowBlox.UICore.ViewModels.AiAssistantHistoryViewModel();
            projectHistory.ConfigureProjectStateAccess(() => new FlowBlox.UICore.ViewModels.AIAssistantProjectStateSnapshot
            {
                ProjectGuid = _project.ProjectGuid
            });
            componentHistory.ConfigureProjectStateAccess(() => new FlowBlox.UICore.ViewModels.AIAssistantProjectStateSnapshot
            {
                ProjectGuid = _project.ProjectGuid,
                ComponentScope = _source.Name
            });
            foreach (var scope in new[] { string.Empty, _source.Name, "AnotherComponent" })
            {
                store.CreateOrUpdateHistory(new AiAssistantHistoryDocument
                {
                    ProjectGuid = _project.ProjectGuid,
                    ComponentScope = scope,
                    Transcripts = [new AssistantTranscriptLine { Kind = AssistantTranscriptKind.User, Text = "Synthetic request" }]
                }, null);
            }
            Assert.AreEqual(1, projectHistory.Histories.Count);
            Assert.AreEqual(string.Empty, projectHistory.Histories.Single().ComponentScope);
            Assert.AreEqual(1, componentHistory.Histories.Count);
            Assert.AreEqual(_source.Name, componentHistory.Histories.Single().ComponentScope);
            store.Refresh(_project.ProjectGuid);
            Assert.AreEqual(1, projectHistory.Histories.Count);
            Assert.AreEqual(1, componentHistory.Histories.Count);
        }
        finally
        {
            option.Value = previous;
        }
    }

    [TestMethod]
    public async Task ComponentContextHashSurvivesRestoreAndDetectsWorkingCopyChanges()
    {
        _working = Open(_source);
        var executor = new QueuedAiExecutor();
        var service = new AiAssistantService(executor, _api, configurationProvider: () => new AssistantConfiguration
        {
            MaxContextTokens = 200000,
            EnableAutomaticAdjustment = false,
            AttachProjectJsonAutomatically = true
        }) { ComponentContext = _api.ComponentContext };
        var history = new AiAssistantHistoryDocument();
        service.RestoreSession(history);

        var first = await service.ExecutePromptAsync("Inspect this block", CancellationToken.None);
        Assert.IsTrue(first.Success, string.Join("\n", first.Errors));
        service.UpdateHistorySessionMetadata(history);
        Assert.IsFalse(string.IsNullOrWhiteSpace(history.LastContextJsonHash));

        var restoredHistory = Newtonsoft.Json.JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(
            Newtonsoft.Json.JsonConvert.SerializeObject(history));
        service.RestoreSession(restoredHistory);
        var second = await service.ExecutePromptAsync("Inspect again", CancellationToken.None);
        Assert.IsTrue(second.Success, string.Join("\n", second.Errors));
        var unchangedPrompt = executor.ChatRequests.Last().Messages.Last(message => message.Role == "user").Content;
        Assert.IsFalse(unchangedPrompt.Contains("Current component JSON:", StringComparison.Ordinal));

        _working.RegularExpression = "ChangedAfterRestore";
        var third = await service.ExecutePromptAsync("Inspect the change", CancellationToken.None);
        Assert.IsTrue(third.Success, string.Join("\n", third.Errors));
        var changedPrompt = executor.ChatRequests.Last().Messages.Last(message => message.Role == "user").Content;
        StringAssert.Contains(changedPrompt, "ChangedAfterRestore");
        service.UpdateHistorySessionMetadata(restoredHistory);
        Assert.AreNotEqual(history.LastContextJsonHash, restoredHistory.LastContextJsonHash);
    }

    [TestMethod]
    public async Task ComponentServiceUsesWorkingSnapshotAndLeavesBootstrapShared()
    {
        _working = Open(_source);
        _working.RegularExpression = "UnsavedLocalPattern";
        var executor = new QueuedAiExecutor();
        var service = new AiAssistantService(executor, _api, configurationProvider: () => new AssistantConfiguration
        {
            MaxContextTokens = 200000,
            MaxToolRounds = 2,
            EnableAutomaticAdjustment = false,
            AttachProjectJsonAutomatically = true
        }) { ComponentContext = _api.ComponentContext };
        var response = await service.ExecutePromptAsync("Configure this block", CancellationToken.None);
        Assert.IsTrue(response.Success, string.Join("\n", response.Errors));
        var user = executor.ChatRequests.Single().Messages.Single(message => message.Role == "user").Content;
        StringAssert.Contains(user, "UnsavedLocalPattern");
        Assert.IsFalse(user.Contains("Current project JSON:", StringComparison.Ordinal));
        Assert.AreEqual("Before", _source.RegularExpression);
    }
}
