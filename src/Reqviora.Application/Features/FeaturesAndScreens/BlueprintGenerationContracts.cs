
using System.Text.Json;
using System.Text.RegularExpressions;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.FeaturesAndScreens;

public sealed record ProposedFeature(
    string Id,
    string Name,
    IReadOnlyList<string> RequirementIds
);

public sealed record ProposedScreen(
    string Id,
    string Name,
    IReadOnlyList<string> FeatureIds
);

public sealed record ProposedComponent(
    string Id,
    string ScreenId,
    string Type,
    string Variant
);

public sealed record ProposedAction(
    string Id,
    string ComponentId,
    string Kind,
    string? NavigationId
);

public sealed record ProposedNavigation(
    string Id,
    string SourceScreenId,
    string TargetScreenId,
    string ActionId
);

public sealed record BlueprintGenerationRequest(
    Guid ProjectId,
    string BlueprintId,
    int Revision,
    IReadOnlyList<ReviewedRequirement> Requirements,
    IReadOnlyList<ProposedFeature> Features,
    IReadOnlyList<ProposedScreen> Screens,
    IReadOnlyList<ProposedComponent> Components,
    IReadOnlyList<ProposedAction> Actions,
    IReadOnlyList<ProposedNavigation> Navigation
);

public interface IDesignComponentRegistry
{
    bool Supports(string type, string variant);
}

// Temporary local starter registry, not the final registry service.

public sealed class Day03StarterComponentRegistry
    : IDesignComponentRegistry
{
    private static readonly Dictionary<string, string[]> Allowed =
        new(StringComparer.Ordinal)
        {
            ["Button"] = ["default", "filled", "outlined", "text"],
            ["Text"] = ["default"],
            ["TextInput"] = ["default", "outlined", "filled"],
            ["Card"] = ["default", "outlined", "elevated"],
            ["List"] = ["default"],
            ["Container"] = ["default"],
            ["Image"] = ["default"],
            ["Dialog"] = ["default"]
        };

    public bool Supports(string type, string variant) =>
        type is not null &&
        variant is not null &&
        Allowed.TryGetValue(type, out var variants) &&
        variants.Contains(variant, StringComparer.Ordinal);
}

public interface IBlueprintDraftGenerator
{
    BlueprintDocument Generate(BlueprintGenerationRequest request);
}

public sealed class BlueprintDraftGenerator(
    IDesignComponentRegistry registry) : IBlueprintDraftGenerator
{
    private static readonly Regex BlueprintIdPattern = new(
        @"^BP-[A-Z0-9]+(-[A-Z0-9]+)*$");

    public BlueprintDocument Generate(
        BlueprintGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ProjectId == Guid.Empty ||
            request.Revision < 1 ||
            string.IsNullOrWhiteSpace(request.BlueprintId) ||
            !BlueprintIdPattern.IsMatch(request.BlueprintId))
        {
            throw new ArgumentException(
                "Invalid Blueprint identity or revision.");
        }

        if (request.Requirements is null ||
            request.Features is null ||
            request.Screens is null ||
            request.Components is null ||
            request.Actions is null ||
            request.Navigation is null)
        {
            throw new ArgumentException(
                "Blueprint collections cannot be null.");
        }

        if (request.Requirements.Count == 0 ||
            request.Features.Count == 0 ||
            request.Screens.Count == 0)
        {
            throw new ArgumentException(
                "Confirmed requirements, features and screens are required.");
        }

        static Dictionary<string, T> Index<T>(
            IEnumerable<T> items,
            Func<T, string> selector,
            string prefix)
        {
            var result = new Dictionary<string, T>(
                StringComparer.Ordinal);

            foreach (var item in items)
            {
                if (item is null)
                    throw new ArgumentException("Null Blueprint item.");

                var id = selector(item);

                if (string.IsNullOrWhiteSpace(id) ||
                    !Regex.IsMatch(
                        id,
                        "^" + prefix + "-[A-Z0-9]+(-[A-Z0-9]+)*$"))
                {
                    throw new ArgumentException(
                        "Invalid ID for " + prefix);
                }

                if (!result.TryAdd(id, item))
                    throw new ArgumentException(
                        "Duplicate Blueprint ID: " + id);
            }

            return result;
        }

        var requirements = Index(
            request.Requirements, x => x.Id, "REQ");

        var features = Index(
            request.Features, x => x.Id, "FEAT");

        var screens = Index(
            request.Screens, x => x.Id, "SCR");

        var components = Index(
            request.Components, x => x.Id, "CMP");

        var actions = Index(
            request.Actions, x => x.Id, "ACT");

        var navigation = Index(
    request.Navigation, x => x.Id, "NAV");

        foreach (var requirement in request.Requirements)
        {
            if (requirement.Status !=
                    RequirementReviewStatus.Confirmed ||
                requirement.ConfirmedBy is null ||
                requirement.ConfirmedBy == Guid.Empty ||
                requirement.ConfirmedAt is null ||
                string.IsNullOrWhiteSpace(requirement.Title) ||
                requirement.Evidence is null ||
                requirement.Evidence.Count == 0)
            {
                throw new ArgumentException(
                    "Only explicitly confirmed requirements can generate a Blueprint.");
            }
        }

        foreach (var feature in request.Features)
        {
            if (string.IsNullOrWhiteSpace(feature.Name) ||
                feature.RequirementIds is null ||
                feature.RequirementIds.Count == 0 ||
                feature.RequirementIds.Distinct(
                    StringComparer.Ordinal).Count() !=
                    feature.RequirementIds.Count ||
                feature.RequirementIds.Any(id =>
                    !requirements.ContainsKey(id)))
            {
                throw new ArgumentException(
                    "Invalid feature/requirement mapping.");
            }
        }

        foreach (var screen in request.Screens)
        {
            if (string.IsNullOrWhiteSpace(screen.Name) ||
                screen.FeatureIds is null ||
                screen.FeatureIds.Count == 0 ||
                screen.FeatureIds.Distinct(
                    StringComparer.Ordinal).Count() !=
                    screen.FeatureIds.Count ||
                screen.FeatureIds.Any(id =>
                    !features.ContainsKey(id)))
            {
                throw new ArgumentException(
                    "Invalid screen/feature mapping.");
            }
        }

        foreach (var component in request.Components)
        {
            if (!screens.ContainsKey(component.ScreenId) ||
                !registry.Supports(
                    component.Type, component.Variant))
            {
                throw new ArgumentException(
                    "Unknown component screen or unsupported component.");
            }
        }

        foreach (var action in request.Actions)
        {
            if (!components.ContainsKey(action.ComponentId))
                throw new ArgumentException(
                    "Action references an unknown component.");
            if (action.Kind is not (
                "navigate" or "submit" or
                "update" or "openDialog" or "closeDialog"))
            {
                throw new ArgumentException("Unsupported mock action.");
            }

            if (action.Kind == "navigate")
            {
                if (action.NavigationId is null ||
                    !navigation.ContainsKey(action.NavigationId))
                {
                    throw new ArgumentException(
                        "Navigation action needs a valid route.");
                }
            }
            else if (action.NavigationId is not null)
            {
                throw new ArgumentException(
                    "Only navigate actions can reference routes.");
            }
        }

        foreach (var route in request.Navigation)
        {
            if (!screens.ContainsKey(route.SourceScreenId) ||
                !screens.ContainsKey(route.TargetScreenId) ||
                !actions.TryGetValue(route.ActionId, out var action))
            {
                throw new ArgumentException(
                    "Broken navigation reference.");
            }

            if (action.Kind != "navigate" ||
                action.NavigationId != route.Id ||
                components[action.ComponentId].ScreenId !=
                    route.SourceScreenId)
            {
                throw new ArgumentException(
                    "Navigation source/action mismatch.");
            }
        }

        return new BlueprintDocument
        {
            SchemaVersion = "1.0.0",
            BlueprintId = request.BlueprintId,
            ProjectId = request.ProjectId,
            Revision = request.Revision,
            Lifecycle = "draft",
            TargetPlatforms = ["web"],

            Requirements = request.Requirements.Select(r =>
                new BlueprintRequirement
                {
                    Id = r.Id,
                    Text = r.Title + "\n" + r.Description,
                    Origin = "ai",
                    Status = "confirmed",
                    ConfirmedBy = r.ConfirmedBy!.Value.ToString(),
                    ConfirmedAt = r.ConfirmedAt,
                    Evidence = r.Evidence.Select(e =>
                        new BlueprintEvidence
                        {
                            SourceId = e.SourceId,
                            Locator = e.Locator,
                            Quote = e.Quote
                        }).ToList()
                }).ToList(),

            Features = request.Features.Select(f =>
                new BlueprintFeature
                {
                    Id = f.Id,
                    Name = f.Name,
                    RequirementIds = f.RequirementIds.ToList()
                }).ToList(),

            Screens = request.Screens.Select(s =>
                new BlueprintScreen
                {
                    Id = s.Id,
                    Name = s.Name,
                    FeatureIds = s.FeatureIds.ToList(),
                    ComponentIds = request.Components
                        .Where(c => c.ScreenId == s.Id)
                        .Select(c => c.Id).ToList()
                }).ToList(),

            Components = request.Components.Select(c =>
                new BlueprintComponent
                {
                    Id = c.Id,
                    ScreenId = c.ScreenId,
                    Type = c.Type,
                    Props = new Dictionary<string, JsonElement>
                    {
                        ["variant"] =
                            JsonSerializer.SerializeToElement(c.Variant)
                    },
                    ActionIds = request.Actions
                        .Where(a => a.ComponentId == c.Id)
                        .Select(a => a.Id).ToList()
                }).ToList(),

            Actions = request.Actions.Select(a =>
                new BlueprintAction
                {
                    Id = a.Id,
                    ComponentId = a.ComponentId,
                    Kind = a.Kind,
                    NavigationId = a.NavigationId
                }).ToList(),

            Navigation = request.Navigation.Select(n =>
                new BlueprintNavigation
                {
                    Id = n.Id,
                    SourceScreenId = n.SourceScreenId,
                    TargetScreenId = n.TargetScreenId,
                    ActionId = n.ActionId
                }).ToList()
        };
    }
}
