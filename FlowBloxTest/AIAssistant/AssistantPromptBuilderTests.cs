using FlowBlox.AIAssistant.Builder;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class AssistantPromptBuilderTests
    {
        [TestMethod]
        public void BuildSessionBootstrapPrompt_RoutesSkillGuidanceAndListsBundledSkillProjects()
        {
            var prompt = AssistantPromptBuilder.BuildSessionBootstrapPrompt([]);

            Assert.IsFalse(prompt.Contains("{{EXPLANATION_ROUTING}}", StringComparison.Ordinal));
            StringAssert.Contains(prompt, "`explaining_skills`");
            StringAssert.Contains(prompt, "FetchUrlDom: Open a URL and return its rendered DOM content.");
            StringAssert.Contains(prompt, "ExecuteSQLQuery: Execute SQL against a database and return the query result as CSV.");
            Assert.IsFalse(prompt.Contains("{{STANDARD_SKILL_PROJECTS}}", StringComparison.Ordinal));
        }

        [TestMethod]
        public void BuildSessionBootstrapPrompt_ListsOnlyOnDemandExplanations()
        {
            var prompt = AssistantPromptBuilder.BuildSessionBootstrapPrompt([]);

            StringAssert.Contains(prompt, "`explaining_generators`");
            Assert.IsFalse(prompt.Contains("`explaining_version_notes`", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("`system_message`", StringComparison.Ordinal));
            Assert.IsFalse(prompt.Contains("`session_bootstrap`", StringComparison.Ordinal));
        }

    }
}
