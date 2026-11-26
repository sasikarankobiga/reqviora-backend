using Reqviora.Application.Features.GuidedBuilder;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class GuidedBuilderValidatorTests
{
    [Fact]
    public void Completion_validation_reports_all_missing_steps()
    {
        var issues = GuidedBuilderValidator.Validate(
            new GuidedBuilderDocument(GuidedBuilderValidator.CurrentVersion), requireComplete: true);

        Assert.Equal(8, issues.Count(x => x.Code == "missing_step"));
    }

    [Fact]
    public void Component_cycles_duplicates_and_unsupported_properties_are_rejected()
    {
        var components = new ComponentsLayoutStep(
            [new BuilderComponentDefinition("Container", ["default"], ["label"], true)],
            [
                Component("CMP-1", "CMP-2", ["CMP-2"], new Dictionary<string, string> { ["unknown"] = "value" }),
                Component("CMP-2", "CMP-1", ["CMP-1"], new Dictionary<string, string>())
            ]);
        var issues = GuidedBuilderValidator.Validate(
            new GuidedBuilderDocument(GuidedBuilderValidator.CurrentVersion, ComponentsLayout: components), false);

        Assert.Contains(issues, x => x.Code == "component_cycle");
        Assert.Contains(issues, x => x.Code == "unsupported_property");
    }

    [Fact]
    public void Null_collections_are_validation_findings_not_server_errors()
    {
        var invalid = new GuidedBuilderDocument(GuidedBuilderValidator.CurrentVersion,
            IdentityAudience: new IdentityAudienceStep("Project", "", "Description", "", "",
                "Problem", null!, ["Audience"], [], [], ["web"], "Scope", [], [], ["Success"], []));

        var issue = Assert.Single(GuidedBuilderValidator.Validate(invalid, false));
        Assert.Equal("invalid_structure", issue.Code);
    }

    private static BuilderComponent Component(string id, string parent, IReadOnlyList<string> children,
        IReadOnlyDictionary<string, string> properties) => new(id, "SCR-1", "Container", "default",
        parent, children, properties, new BuilderLayout("stack", 1, "sm", null),
        new Dictionary<string, BuilderLayout>(), false, []);
}
