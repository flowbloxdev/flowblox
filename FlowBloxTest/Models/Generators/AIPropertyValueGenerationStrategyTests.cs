using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.AIRemote;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.Json;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Generators;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Test.Runtime;
using FlowBloxTest.FlowBlocks;
using FlowBloxTest.FlowBlocks.Execution;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;

namespace FlowBloxTest.Models.Generators
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class AIPropertyValueGenerationStrategyTests : FlowBloxTestsBase
    {
        private FlowBloxProject _project;

        [TestInitialize]
        public void TestInitialize()
        {
            _project = new FlowBloxProject();
            FlowBloxProjectManager.Instance.ActiveProject = _project;
        }

        [TestMethod]
        public void IncludesExpectedValueInPrompt()
        {
            var startFlowBlock = CreateFlowBlock<StartFlowBlock>();
            var resultBlock = CreateFlowBlock<ExecutionOrderTestFlowBlock>(startFlowBlock);
            var expectedValue = "w123-280e-1955-c001";
            var provider = new CapturingAIProvider();
            var strategy = new AIPropertyValueGenerationStrategy(resultBlock)
            {
                Provider = provider,
                PromptTemplate = "$GenerationStrategy::TestExpectations"
            };
            var testDefinition = new FlowBloxTestDefinition
            {
                Name = "AI expected value prompt",
                Entries = new ObservableCollection<FlowBlockTestDataset>
                {
                    new()
                    {
                        FlowBlock = resultBlock,
                        FlowBloxTestConfigurations = new List<FlowBloxFieldTestConfiguration>
                        {
                            new()
                            {
                                FieldElement = resultBlock.ResultField,
                                SelectionMode = FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue,
                                UserInput = expectedValue,
                                ExpectationConditions = new ObservableCollection<ExpectationCondition>
                                {
                                    new()
                                    {
                                        ExpectationConditionTarget = ExpectationConditionTarget.NumberOfDatasets,
                                        Operator = ComparisonOperator.Equals,
                                        Value = "6"
                                    }
                                }
                            }
                        }
                    }
                }
            };
            var runtime = new FlowBloxUnitTestRuntime(_project);

            strategy.Execute(
                runtime,
                new Dictionary<FlowBloxTestDefinition, FlowBloxTestResult>
                {
                    [testDefinition] = new(false, new Dictionary<string, string>())
                });

            Assert.IsTrue(provider.LastPrompt?.Contains($"ExpectedValue={expectedValue}") == true);
            Assert.IsTrue(provider.LastPrompt?.Contains("NumberOfDatasets Equals 6") == true);
        }

        [TestMethod]
        public void AppliesQuickUpdateForWebSelectorProperties()
        {
            var webSelector = new WebSelectorFlowBlock();
            var strategy = new AIPropertyValueGenerationStrategy(webSelector);

            strategy.Assign("""
            {
              "JsonContract": "FlowBloxQuickUpdate",
              "Properties": {
                "XPath": "//a[contains(@class,'card-head-url')]",
                "OutputMode": "Attribute",
                "AttributeName": "href"
              }
            }
            """);

            Assert.AreEqual("//a[contains(@class,'card-head-url')]", webSelector.XPath);
            Assert.AreEqual(WebSelectorOutputMode.Attribute, webSelector.OutputMode);
            Assert.AreEqual("href", webSelector.AttributeName);
        }

        [TestMethod]
        public void AppliesQuickUpdateUsingProviderInstructionParser()
        {
            var webSelector = new WebSelectorFlowBlock();
            var strategy = new AIPropertyValueGenerationStrategy(webSelector)
            {
                Provider = new QuickUpdateInstructionProvider()
            };

            strategy.Assign("<provider-specific-instruction-format />");

            Assert.AreEqual("//a[@data-id]", webSelector.XPath);
            Assert.AreEqual(WebSelectorOutputMode.Attribute, webSelector.OutputMode);
            Assert.AreEqual("data-id", webSelector.AttributeName);
        }

        [TestMethod]
        public void AppliesQuickUpdateForStartEndPatternCollection()
        {
            var selector = new StartEndPatternSelectorFlowBlock();
            selector.StartEndPatterns.Add(new StartEndPattern { StartPattern = "old" });
            var strategy = new AIPropertyValueGenerationStrategy(selector);

            strategy.Assign("""
            {
              "JsonContract": "FlowBloxQuickUpdate",
              "Properties": {
                "StartEndPatterns": [
                  {
                    "StartPattern": "<item>",
                    "EndPattern": "</item>",
                    "Index": 0,
                    "ReturnOptions": "StartAndEndPattern"
                  },
                  {
                    "StartPattern": "<name>",
                    "EndPattern": "</name>",
                    "Index": null,
                    "ReturnOptions": "StartPattern"
                  }
                ]
              }
            }
            """);

            Assert.AreEqual(2, selector.StartEndPatterns.Count);
            Assert.AreEqual("<item>", selector.StartEndPatterns[0].StartPattern);
            Assert.AreEqual("</item>", selector.StartEndPatterns[0].EndPattern);
            Assert.AreEqual(0, selector.StartEndPatterns[0].Index);
            Assert.AreEqual(StartEndPatternReturnOptions.StartAndEndPattern, selector.StartEndPatterns[0].ReturnOptions);
            Assert.AreEqual("<name>", selector.StartEndPatterns[1].StartPattern);
            Assert.AreEqual("</name>", selector.StartEndPatterns[1].EndPattern);
            Assert.IsNull(selector.StartEndPatterns[1].Index);
            Assert.AreEqual(StartEndPatternReturnOptions.StartPattern, selector.StartEndPatterns[1].ReturnOptions);
        }

        [TestMethod]
        public void IncludesAllReferencedFieldValuesAndMasksPasswords()
        {
            var start = CreateFlowBlock<StartFlowBlock>();
            var visibleSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(start);
            var passwordSource = CreateFlowBlock<ExecutionOrderTestFlowBlock>(visibleSource);
            passwordSource.ResultField.IsPassword = true;

            var target = CreateFlowBlock<JsonObjectWriterFlowBlock>(passwordSource);
            target.Assignments.Add(new JsonPropertyValueAssignment
            {
                PropertyName = "visible",
                FieldValue = visibleSource.ResultField
            });
            target.Assignments.Add(new JsonPropertyValueAssignment
            {
                PropertyName = "secret",
                Value = passwordSource.ResultField.FullyQualifiedName
            });

            var provider = new CapturingAIProvider();
            var strategy = new AIPropertyValueGenerationStrategy(target)
            {
                Provider = provider,
                PromptTemplate = "$GenerationStrategy::TargetFlowBlockInputValues" + Environment.NewLine +
                    $"DirectPasswordReference={passwordSource.ResultField.FullyQualifiedName}"
            };
            var testDefinition = new FlowBloxTestDefinition
            {
                Name = "Referenced fields",
                Entries = new ObservableCollection<FlowBlockTestDataset>
                {
                    new()
                    {
                        FlowBlock = target,
                        FlowBloxTestConfigurations =
                        [
                            new FlowBloxFieldTestConfiguration
                            {
                                FieldElement = target.ResultField,
                                SelectionMode = FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue,
                                UserInput = "expected"
                            }
                        ]
                    }
                }
            };
            var runtime = new FlowBloxUnitTestRuntime(_project);

            strategy.Execute(
                runtime,
                new Dictionary<FlowBloxTestDefinition, FlowBloxTestResult>
                {
                    [testDefinition] = new(
                        false,
                        new Dictionary<string, string>
                        {
                            [visibleSource.ResultField.FullyQualifiedName] = "visible-value",
                            [passwordSource.ResultField.FullyQualifiedName] = "secret-value"
                        })
                });

            StringAssert.Contains(
                provider.LastPrompt,
                $"{visibleSource.ResultField.FullyQualifiedName}=visible-value");
            StringAssert.Contains(
                provider.LastPrompt,
                $"{passwordSource.ResultField.FullyQualifiedName}={GlobalConstants.HiddenSensitiveValue}");
            StringAssert.Contains(
                provider.LastPrompt,
                $"DirectPasswordReference={GlobalConstants.HiddenSensitiveValue}");
            Assert.IsFalse(provider.LastPrompt?.Contains("secret-value", StringComparison.Ordinal) == true);
        }

        private sealed class CapturingAIProvider : AIProviderBase
        {
            public override string ProviderType => "Test";

            public string LastPrompt { get; private set; }

            protected override Task<AIResponse> ExecuteChatCoreAsync(AIChatRequest request, CancellationToken ct)
            {
                LastPrompt = request.Messages.LastOrDefault()?.Content;
                return Task.FromResult(new AIResponse
                {
                    Success = true,
                    Text = "generated"
                });
            }
        }

        private sealed class QuickUpdateInstructionProvider : AIProviderBase
        {
            public override string ProviderType => "Test";

            public override bool TryParseInstruction(string output, Exception? primaryParseException, out JObject instructionJson)
            {
                instructionJson = JObject.Parse("""
                {
                  "JsonContract": "FlowBloxQuickUpdate",
                  "Properties": {
                    "XPath": "//a[@data-id]",
                    "OutputMode": "Attribute",
                    "AttributeName": "data-id"
                  }
                }
                """);
                return true;
            }

            protected override Task<AIResponse> ExecuteChatCoreAsync(AIChatRequest request, CancellationToken ct)
            {
                return Task.FromResult(new AIResponse
                {
                    Success = true,
                    Text = string.Empty
                });
            }
        }
    }
}
