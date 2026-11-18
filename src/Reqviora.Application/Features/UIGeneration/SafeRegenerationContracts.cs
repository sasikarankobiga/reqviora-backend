
using System.Text.Json;
using Reqviora.Application.Features.FeaturesAndScreens;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;

namespace Reqviora.Application.Features.UIGeneration;

public sealed record SafeRegenerationRequest(
    int ExpectedRevision,
    IReadOnlyCollection<string> ProtectedFeatureIds,
    IReadOnlyCollection<string> ProtectedScreenIds,
    IReadOnlyCollection<string> ProtectedComponentIds
);

public sealed record SafeRegenerationResult(
    bool Applied,
    string Reason,
    BlueprintDocument Blueprint
);

public sealed class SafeRegenerationService(
    IDesignComponentRegistry registry)
{
    public SafeRegenerationResult TryApply(
        BlueprintDocument current,
        BlueprintDocument proposed,
        SafeRegenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(request);

        SafeRegenerationResult Reject(string reason) =>
            new(false, reason, current);

        if (request.ProtectedFeatureIds is null ||
            request.ProtectedScreenIds is null ||
            request.ProtectedComponentIds is null)
            return Reject("INVALID_PROTECTION_POLICY");

        if (current.Revision != request.ExpectedRevision)
            return Reject("REVISION_CONFLICT");

        if (current.Lifecycle != "draft")
            return Reject("IMMUTABLE_REVISION");

        if (proposed.Lifecycle != "draft")
            return Reject("INVALID_PROPOSED_LIFECYCLE");

        if (proposed.Revision != current.Revision + 1 ||
            proposed.BlueprintId != current.BlueprintId ||
            proposed.ProjectId != current.ProjectId ||
            proposed.SchemaVersion != current.SchemaVersion)
            return Reject("BLUEPRINT_IDENTITY_MISMATCH");

        if (current.Requirements is null ||
            proposed.Requirements is null ||
            current.Features is null ||
            proposed.Features is null ||
            current.Screens is null ||
            proposed.Screens is null ||
            current.Components is null ||
            proposed.Components is null ||
            proposed.Actions is null ||
            proposed.Navigation is null)
            return Reject("INVALID_COLLECTION");

        // AI cannot create, alter, or remove a human confirmation.
        var oldConfirmed = current.Requirements
            .Where(r => r.Status == "confirmed")
            .ToDictionary(r => r.Id, StringComparer.Ordinal);

        var newConfirmed = proposed.Requirements
            .Where(r => r.Status == "confirmed")
            .ToDictionary(r => r.Id, StringComparer.Ordinal);

        if (oldConfirmed.Count != newConfirmed.Count)
            return Reject("CONFIRMATION_CHANGED");

        foreach (var (id, oldRequirement) in oldConfirmed)
        {
            if (!newConfirmed.TryGetValue(id, out var newer) ||
                !Same(oldRequirement, newer))
                return Reject("CONFIRMATION_CHANGED");
        }

        if (!ProtectedUnchanged(
            current.Features,
            proposed.Features,
            request.ProtectedFeatureIds,
            f => f.Id))
            return Reject("PROTECTED_FEATURE_CHANGED");

        if (!ProtectedUnchanged(
            current.Screens,
            proposed.Screens,
            request.ProtectedScreenIds,
            s => s.Id))
            return Reject("PROTECTED_SCREEN_CHANGED");

        if (!ProtectedUnchanged(
            current.Components,
            proposed.Components,
            request.ProtectedComponentIds,
            c => c.Id))
            return Reject("PROTECTED_COMPONENT_CHANGED");

        if (!ValidGraph(proposed))
            return Reject("INVALID_GENERATED_GRAPH");

        return new SafeRegenerationResult(
            true, "APPLIED_AS_NEW_DRAFT", proposed);
    }

    private static bool Same<T>(T left, T right) =>
        JsonSerializer.Serialize(left) ==
        JsonSerializer.Serialize(right);

    private static bool ProtectedUnchanged<T>(
        IReadOnlyCollection<T> oldItems,
        IReadOnlyCollection<T> newItems,
        IReadOnlyCollection<string> protectedIds,
        Func<T, string> idSelector)
    {
        foreach (var id in protectedIds)
        {
            var previous = oldItems.FirstOrDefault(
                x => idSelector(x) == id);

            var next = newItems.FirstOrDefault(
                x => idSelector(x) == id);

            if (previous is null || next is null ||
                !Same(previous, next))
                return false;
        }

        return true;
    }

    private bool ValidGraph(BlueprintDocument draft)
    {
        try
        {
            if (draft.Requirements is null ||
                draft.Features is null ||
                draft.Screens is null ||
                draft.Components is null ||
                draft.Actions is null ||
                draft.Navigation is null ||
                draft.TargetPlatforms is null)
                return false;

            if (!draft.TargetPlatforms.SequenceEqual(["web"]))
                return false;

            if (!Unique(draft.Requirements.Select(x => x.Id)) ||
                !Unique(draft.Features.Select(x => x.Id)) ||
                !Unique(draft.Screens.Select(x => x.Id)) ||
                !Unique(draft.Components.Select(x => x.Id)) ||
                !Unique(draft.Actions.Select(x => x.Id)) ||
                !Unique(draft.Navigation.Select(x => x.Id)))
                return false;

            var req = draft.Requirements.ToDictionary(x => x.Id);
            var features = draft.Features.ToDictionary(x => x.Id);
            var screens = draft.Screens.ToDictionary(x => x.Id);
            var components = draft.Components.ToDictionary(x => x.Id);
            var actions = draft.Actions.ToDictionary(x => x.Id);
            var routes = draft.Navigation.ToDictionary(x => x.Id);

            foreach (var feature in draft.Features)
            {
                if (feature.RequirementIds is null ||
                    feature.RequirementIds.Count == 0 ||
                    !Unique(feature.RequirementIds) ||
                    feature.RequirementIds.Any(id =>
                        !req.TryGetValue(id, out var r) ||
                        r.Status != "confirmed"))
                    return false;
            }

            foreach (var screen in draft.Screens)
            {
                if (screen.FeatureIds is null ||
                    screen.ComponentIds is null ||
                    !Unique(screen.FeatureIds) ||
                    !Unique(screen.ComponentIds) ||
                    screen.FeatureIds.Any(id =>
                        !features.ContainsKey(id)) ||
                    screen.ComponentIds.Any(id =>
                        !components.TryGetValue(id, out var c) ||
                        c.ScreenId != screen.Id))
                    return false;
            }

            foreach (var component in draft.Components)
            {
                if (!screens.TryGetValue(
                    component.ScreenId, out var screen))
                    return false;

                if (!screen.ComponentIds.Contains(component.Id))
                    return false;

                if (component.Props is null ||
                    !component.Props.TryGetValue(
                        "variant", out var variantJson) ||
                    variantJson.ValueKind != JsonValueKind.String)
                    return false;

                var variant = variantJson.GetString()!;

                if (!registry.Supports(component.Type, variant))
                    return false;

                if (component.ActionIds is null ||
                    !Unique(component.ActionIds) ||
                    component.ActionIds.Any(id =>
                        !actions.TryGetValue(id, out var a) ||
                        a.ComponentId != component.Id))
                    return false;
            }

            foreach (var action in draft.Actions)
            {
                if (!components.TryGetValue(
                    action.ComponentId, out var component) ||
                    !component.ActionIds.Contains(action.Id))
                    return false;

                if (action.Kind is not (
                    "navigate" or "back" or
                    "mock_submit" or "show_message"))
                    return false;

                if (action.Kind == "navigate")
                {
                    if (action.NavigationId is null ||
                        !routes.ContainsKey(action.NavigationId))
                        return false;
                }
                else if (action.NavigationId is not null)
                    return false;
            }

            foreach (var route in draft.Navigation)
            {
                if (!screens.ContainsKey(route.SourceScreenId) ||
                    !screens.ContainsKey(route.TargetScreenId) ||
                    !actions.TryGetValue(route.ActionId, out var action))
                    return false;

                if (action.Kind != "navigate" ||
                    action.NavigationId != route.Id ||
                    components[action.ComponentId].ScreenId !=
                        route.SourceScreenId)
                    return false;
            }

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Unique(IEnumerable<string> ids)
    {
        var list = ids.ToArray();

        return list.All(x => !string.IsNullOrWhiteSpace(x)) &&
            list.Distinct(StringComparer.Ordinal).Count() ==
            list.Length;
    }
}
