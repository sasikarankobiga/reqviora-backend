using System.Text.Json;
using System.Text.Json.Serialization;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;

namespace Reqviora.Application.Features.FeaturesAndScreens;

// Deterministic initial layout, not an AI-generated semantic design.
// The caller supplies only server-loaded, confirmed requirements.
public static class InitialBlueprintPlanner
{
    public static BlueprintDocument Generate(IBlueprintDraftGenerator generator, Guid projectId,
        IReadOnlyList<ReviewedRequirement> requirements)
    {
        if (requirements.Count is < 1 or > 50)
            throw new ArgumentException("Select between one and fifty confirmed requirements.");

        var features = new List<ProposedFeature>();
        var screens = new List<ProposedScreen>();
        var components = new List<ProposedComponent>();
        var actions = new List<ProposedAction>();
        var routes = new List<ProposedNavigation>();
        for (var i = 0; i < requirements.Count; i++)
        {
            var requirement = requirements[i];
            var feature = $"FEAT-{i + 1}";
            var screen = $"SCR-{i + 1}";
            features.Add(new(feature, requirement.Title, new[] { requirement.Id }));
            screens.Add(new(screen, requirement.Title, new[] { feature }));
            components.Add(new($"CMP-{i + 1}-TITLE", screen, "Text", "default"));
            components.Add(new($"CMP-{i + 1}-DESCRIPTION", screen, "Text", "default"));
            if (i + 1 < requirements.Count)
            {
                var button = $"CMP-{i + 1}-NEXT";
                var action = $"ACT-{i + 1}-NEXT";
                var route = $"NAV-{i + 1}-NEXT";
                components.Add(new(button, screen, "Button", "filled"));
                actions.Add(new(action, button, "navigate", route));
                routes.Add(new(route, screen, $"SCR-{i + 2}", action));
            }
        }

        var blueprint = generator.Generate(new(projectId, $"BP-{Guid.NewGuid():N}".ToUpperInvariant(), 1,
            requirements, features, screens, components, actions, routes));
        var byId = blueprint.Components.ToDictionary(component => component.Id, StringComparer.Ordinal);
        for (var i = 0; i < requirements.Count; i++)
        {
            byId[$"CMP-{i + 1}-TITLE"].Props["text"] = JsonSerializer.SerializeToElement(requirements[i].Title);
            byId[$"CMP-{i + 1}-DESCRIPTION"].Props["text"] = JsonSerializer.SerializeToElement(requirements[i].Description);
            if (i + 1 < requirements.Count)
                byId[$"CMP-{i + 1}-NEXT"].Props["text"] = JsonSerializer.SerializeToElement("Next");
        }
        // Validate the final JSON, including the component text added above, before persistence.
        var json = JsonSerializer.Serialize(blueprint, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        var validated = BlueprintJsonContract.Parse(json);
        var result = BlueprintContractValidator.Validate(validated);
        if (!result.IsValid)
            throw new ArgumentException(result.Issues[0].Message);
        return validated;
    }
}
