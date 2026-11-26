using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reqviora.Application.Features.GuidedBuilder;

public sealed record BuilderSourceReference(string SourceId, string Locator, string? Quote);
public sealed record BuilderPersona(string Id, string Name, string Description, IReadOnlyList<string> Goals);
public sealed record BuilderRole(string Id, string Name, string Description);

public sealed record IdentityAudienceStep(
    string ProjectName, string Tagline, string Description, string Category, string Industry,
    string ProblemStatement, IReadOnlyList<string> Goals, IReadOnlyList<string> TargetAudience,
    IReadOnlyList<BuilderPersona> Personas, IReadOnlyList<BuilderRole> Roles,
    IReadOnlyList<string> Platforms, string Scope, IReadOnlyList<string> Constraints,
    IReadOnlyList<string> Assumptions, IReadOnlyList<string> SuccessCriteria,
    IReadOnlyList<BuilderSourceReference> SourceReferences);

public sealed record BuilderRequirement(string Id, string Text, string Priority, bool Confirmed);
public sealed record BuilderFeature(string Id, string Name, string Description, IReadOnlyList<string> RequirementIds);
public sealed record BuilderScreen(string Id, string Name, string Purpose,
    IReadOnlyList<string> FeatureIds, IReadOnlyList<string> AllowedRoleIds);
public sealed record FeaturesScreensStep(IReadOnlyList<BuilderRequirement> Requirements,
    IReadOnlyList<BuilderFeature> Features, IReadOnlyList<BuilderScreen> Screens);

public sealed record BuilderRoute(string Id, string SourceScreenId, string TargetScreenId,
    string Trigger, string Outcome, string? Condition, IReadOnlyList<string> AllowedRoleIds);
public sealed record NavigationStep(string InitialScreenId, IReadOnlyList<BuilderRoute> Routes);

public sealed record BuilderTypography(string Token, string FontFamily, int Size, int Weight, double LineHeight);
public sealed record BuilderBorder(string Token, string ColorToken, double Width, string Style, double Radius);
public sealed record BuilderElevation(string Token, double X, double Y, double Blur, double Spread, string Color);
public sealed record BuilderBreakpoint(string Name, int MinWidth);
public sealed record BuilderBrandAsset(string Id, string Kind, string StorageReference, string AltText);
public sealed record BuilderAccessibility(string MinimumContrast, bool ReducedMotion, bool KeyboardNavigation,
    bool ScreenReaderLabels);
public sealed record ThemeBrandStep(
    IReadOnlyDictionary<string, string> Palette, IReadOnlyDictionary<string, string> SemanticColors,
    IReadOnlyList<BuilderTypography> Typography, IReadOnlyDictionary<string, double> Spacing,
    IReadOnlyList<BuilderBorder> Borders, IReadOnlyList<BuilderElevation> Elevation,
    IReadOnlyList<BuilderBreakpoint> Breakpoints, IReadOnlyList<BuilderBrandAsset> BrandAssets,
    BuilderAccessibility Accessibility);

public sealed record BuilderComponentDefinition(string Type, IReadOnlyList<string> Variants,
    IReadOnlyList<string> SupportedProperties, bool AllowsChildren);
public sealed record BuilderLayout(string Mode, int Columns, string GapToken, string? Breakpoint);
public sealed record BuilderComponent(string Id, string ScreenId, string Type, string Variant,
    string? ParentId, IReadOnlyList<string> ChildIds, IReadOnlyDictionary<string, string> Properties,
    BuilderLayout Layout, IReadOnlyDictionary<string, BuilderLayout> ResponsiveLayouts,
    bool Protected, IReadOnlyList<string> EditableProperties);
public sealed record ComponentsLayoutStep(IReadOnlyList<BuilderComponentDefinition> Registry,
    IReadOnlyList<BuilderComponent> Components);

public sealed record BuilderFieldValidation(string Rule, string? Value, string Message);
public sealed record BuilderFormField(string Id, string ComponentId, string Name, string DataType,
    bool Required, IReadOnlyList<BuilderFieldValidation> Validations);
public sealed record BuilderAction(string Id, string ComponentId, string Trigger, string Type,
    string? TargetRouteId, string? Condition, IReadOnlyList<string> AllowedRoleIds);
public sealed record BuilderUiState(string Id, string ScreenId, string Kind, string Description,
    IReadOnlyList<string> VisibleComponentIds);
public sealed record BuilderStateTransition(string Id, string FromStateId, string ToStateId,
    string ActionId, string? Condition);
public sealed record InteractionsStatesStep(IReadOnlyList<BuilderFormField> FormFields,
    IReadOnlyList<BuilderAction> Actions, IReadOnlyList<BuilderUiState> States,
    IReadOnlyList<BuilderStateTransition> Transitions);

public sealed record BuilderAlternative(string Id, string Label, string BlueprintVersionId);
public sealed record AiDesignWorkspaceStep(string BlueprintSchemaVersion,
    IReadOnlyList<string> ProtectedComponentIds, IReadOnlyDictionary<string, IReadOnlyList<string>> EditableProperties,
    IReadOnlyList<BuilderAlternative> Alternatives, bool AutosaveEnabled, int AutosaveSeconds,
    IReadOnlyList<string> UndoVersionIds, IReadOnlyList<string> RedoVersionIds,
    IReadOnlyList<string> RequirementTraceIds);

public sealed record BuilderPreviewScreen(string ScreenId, string Title, IReadOnlyList<string> ComponentIds,
    IReadOnlyList<string> StateIds);
public sealed record BuilderPreviewNavigation(string RouteId, string SourceScreenId, string TargetScreenId, string Trigger);
public sealed record BuilderReviewDecision(string Id, string SubjectId, string Decision, string Comment,
    string ReviewerId, DateTimeOffset DecidedAt);
public sealed record ReviewPreviewStep(IReadOnlyList<BuilderPreviewScreen> Screens,
    IReadOnlyList<BuilderPreviewNavigation> Navigation, IReadOnlyList<BuilderReviewDecision> Decisions,
    int RequirementCount, int CoveredRequirementCount, bool ReadyForApproval);

public sealed record GuidedBuilderDocument(
    string SchemaVersion,
    IdentityAudienceStep? IdentityAudience = null,
    FeaturesScreensStep? FeaturesScreens = null,
    NavigationStep? Navigation = null,
    ThemeBrandStep? ThemeBrand = null,
    ComponentsLayoutStep? ComponentsLayout = null,
    InteractionsStatesStep? InteractionsStates = null,
    AiDesignWorkspaceStep? AiDesignWorkspace = null,
    ReviewPreviewStep? ReviewPreview = null);

public sealed record BuilderValidationIssue(string Code, string Path, string Message);

public static partial class GuidedBuilderValidator
{
    public const string CurrentVersion = "1.0";
    private static readonly string[] Outcomes = ["back", "next", "cancel", "success", "error", "custom"];
    private static readonly string[] StateKinds = ["default", "loading", "empty", "success", "error", "disabled"];

    public static IReadOnlyList<BuilderValidationIssue> Validate(GuidedBuilderDocument document, bool requireComplete)
    {
        try
        {
            return ValidateCore(document, requireComplete);
        }
        catch (Exception exception) when (exception is NullReferenceException or ArgumentNullException)
        {
            return [new BuilderValidationIssue("invalid_structure", "guidedBuilder",
                "Builder arrays, dictionaries and nested objects cannot be null.")];
        }
    }

    private static IReadOnlyList<BuilderValidationIssue> ValidateCore(GuidedBuilderDocument document, bool requireComplete)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<BuilderValidationIssue>();
        if (document.SchemaVersion != CurrentVersion)
            Add(issues, "unsupported_schema", "schemaVersion", "Only guided-builder schema version 1.0 is supported.");
        if (requireComplete)
        {
            if (document.IdentityAudience is null) Missing(issues, "identityAudience");
            if (document.FeaturesScreens is null) Missing(issues, "featuresScreens");
            if (document.Navigation is null) Missing(issues, "navigation");
            if (document.ThemeBrand is null) Missing(issues, "themeBrand");
            if (document.ComponentsLayout is null) Missing(issues, "componentsLayout");
            if (document.InteractionsStates is null) Missing(issues, "interactionsStates");
            if (document.AiDesignWorkspace is null) Missing(issues, "aiDesignWorkspace");
            if (document.ReviewPreview is null) Missing(issues, "reviewPreview");
        }
        var identity = document.IdentityAudience;
        var features = document.FeaturesScreens;
        var navigation = document.Navigation;
        var theme = document.ThemeBrand;
        var components = document.ComponentsLayout;
        var interactions = document.InteractionsStates;
        if (identity is not null) ValidateIdentity(identity, issues);
        if (features is not null) ValidateFeatures(features, identity, issues);
        if (navigation is not null) ValidateNavigation(navigation, features, identity, issues);
        if (theme is not null) ValidateTheme(theme, issues);
        if (components is not null) ValidateComponents(components, features, theme, issues);
        if (interactions is not null) ValidateInteractions(interactions, components, navigation, features, issues);
        if (document.AiDesignWorkspace is { } workspace) ValidateWorkspace(workspace, components, features, issues);
        if (document.ReviewPreview is { } review) ValidateReview(review, components, interactions, navigation, features, issues);
        if (requireComplete && features is not null)
        {
            var covered = features.Features.SelectMany(x => x.RequirementIds).ToHashSet();
            foreach (var requirement in features.Requirements.Where(x => !covered.Contains(x.Id)))
                Add(issues, "uncovered_requirement", $"featuresScreens.requirements[{requirement.Id}]", "Requirement is not mapped to a feature.");
            var usedScreens = navigation?.Routes.SelectMany(x => new[] { x.SourceScreenId, x.TargetScreenId }).ToHashSet() ?? [];
            if (navigation is not null) usedScreens.Add(navigation.InitialScreenId);
            foreach (var screen in features.Screens.Where(x => !usedScreens.Contains(x.Id)))
                Add(issues, "unused_screen", $"featuresScreens.screens[{screen.Id}]", "Screen is not present in the navigation graph.");
        }
        return issues;
    }

    public static void RequireValid(GuidedBuilderDocument document, bool requireComplete = false)
    {
        var issues = Validate(document, requireComplete);
        if (issues.Count != 0)
            throw new ArgumentException(string.Join(" ", issues.Take(5).Select(x => $"{x.Path}: {x.Message}")));
    }

    private static void ValidateIdentity(IdentityAudienceStep value, List<BuilderValidationIssue> issues)
    {
        Required(issues, value.ProjectName, "identityAudience.projectName");
        Required(issues, value.Description, "identityAudience.description");
        Required(issues, value.ProblemStatement, "identityAudience.problemStatement");
        NonEmpty(issues, value.Goals, "identityAudience.goals");
        NonEmpty(issues, value.TargetAudience, "identityAudience.targetAudience");
        NonEmpty(issues, value.Platforms, "identityAudience.platforms");
        NonEmpty(issues, value.SuccessCriteria, "identityAudience.successCriteria");
        Unique(issues, value.Personas.Select(x => x.Id), "identityAudience.personas");
        Unique(issues, value.Roles.Select(x => x.Id), "identityAudience.roles");
        foreach (var source in value.SourceReferences)
            if (Blank(source.SourceId) || Blank(source.Locator)) Add(issues, "invalid_source", "identityAudience.sourceReferences", "Source ID and locator are required.");
    }

    private static void ValidateFeatures(FeaturesScreensStep value, IdentityAudienceStep? identity,
        List<BuilderValidationIssue> issues)
    {
        Unique(issues, value.Requirements.Select(x => x.Id), "featuresScreens.requirements");
        Unique(issues, value.Features.Select(x => x.Id), "featuresScreens.features");
        Unique(issues, value.Screens.Select(x => x.Id), "featuresScreens.screens");
        var requirements = value.Requirements.Select(x => x.Id).ToHashSet();
        var features = value.Features.Select(x => x.Id).ToHashSet();
        var roles = identity?.Roles.Select(x => x.Id).ToHashSet() ?? [];
        foreach (var feature in value.Features)
            References(issues, feature.RequirementIds, requirements, $"featuresScreens.features[{feature.Id}].requirementIds");
        foreach (var screen in value.Screens)
        {
            References(issues, screen.FeatureIds, features, $"featuresScreens.screens[{screen.Id}].featureIds");
            if (identity is not null) References(issues, screen.AllowedRoleIds, roles, $"featuresScreens.screens[{screen.Id}].allowedRoleIds");
        }
    }

    private static void ValidateNavigation(NavigationStep value, FeaturesScreensStep? features,
        IdentityAudienceStep? identity,
        List<BuilderValidationIssue> issues)
    {
        Unique(issues, value.Routes.Select(x => x.Id), "navigation.routes");
        var screens = features?.Screens.Select(x => x.Id).ToHashSet() ?? [];
        if (features is not null)
        {
            References(issues, [value.InitialScreenId], screens, "navigation.initialScreenId");
            foreach (var route in value.Routes)
            {
                References(issues, [route.SourceScreenId, route.TargetScreenId], screens, $"navigation.routes[{route.Id}]");
                if (identity is not null) References(issues, route.AllowedRoleIds,
                    identity.Roles.Select(x => x.Id).ToHashSet(), $"navigation.routes[{route.Id}].allowedRoleIds");
                if (!Outcomes.Contains(route.Outcome)) Add(issues, "unsupported_outcome", $"navigation.routes[{route.Id}].outcome", "Unsupported navigation outcome.");
            }
            var reachable = new HashSet<string>([value.InitialScreenId]);
            while (true)
            {
                var before = reachable.Count;
                reachable.UnionWith(value.Routes.Where(x => reachable.Contains(x.SourceScreenId)).Select(x => x.TargetScreenId));
                if (reachable.Count == before) break;
            }
            foreach (var screen in screens.Except(reachable)) Add(issues, "unreachable_screen", $"featuresScreens.screens[{screen}]", "Screen is unreachable from the initial screen.");
        }
    }

    private static void ValidateTheme(ThemeBrandStep value, List<BuilderValidationIssue> issues)
    {
        foreach (var color in value.Palette)
            if (!HexColor().IsMatch(color.Value)) Add(issues, "invalid_color", $"themeBrand.palette.{color.Key}", "Color must use #RRGGBB or #RRGGBBAA.");
        References(issues, value.SemanticColors.Values, value.Palette.Keys.ToHashSet(), "themeBrand.semanticColors");
        Unique(issues, value.Typography.Select(x => x.Token), "themeBrand.typography");
        Unique(issues, value.Borders.Select(x => x.Token), "themeBrand.borders");
        Unique(issues, value.Elevation.Select(x => x.Token), "themeBrand.elevation");
        Unique(issues, value.Breakpoints.Select(x => x.Name), "themeBrand.breakpoints");
        if (value.Breakpoints.Any(x => x.MinWidth < 0) || value.Breakpoints.Select(x => x.MinWidth).Distinct().Count() != value.Breakpoints.Count)
            Add(issues, "invalid_breakpoints", "themeBrand.breakpoints", "Breakpoint widths must be non-negative and unique.");
        if (value.Spacing.Any(x => x.Value < 0)) Add(issues, "invalid_spacing", "themeBrand.spacing", "Spacing values cannot be negative.");
        if (value.Accessibility.MinimumContrast is not ("AA" or "AAA")) Add(issues, "invalid_contrast", "themeBrand.accessibility.minimumContrast", "Minimum contrast must be AA or AAA.");
    }

    private static void ValidateComponents(ComponentsLayoutStep value, FeaturesScreensStep? features,
        ThemeBrandStep? theme, List<BuilderValidationIssue> issues)
    {
        Unique(issues, value.Registry.Select(x => x.Type), "componentsLayout.registry");
        Unique(issues, value.Components.Select(x => x.Id), "componentsLayout.components");
        var definitions = value.Registry.GroupBy(x => x.Type, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var components = value.Components.GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var screens = features?.Screens.Select(x => x.Id).ToHashSet() ?? [];
        foreach (var component in value.Components)
        {
            if (!definitions.TryGetValue(component.Type, out var definition)) Add(issues, "unsupported_component", $"componentsLayout.components[{component.Id}].type", "Component type is not registered.");
            else
            {
                if (!definition.Variants.Contains(component.Variant)) Add(issues, "unsupported_variant", $"componentsLayout.components[{component.Id}].variant", "Component variant is not registered.");
                foreach (var property in component.Properties.Keys.Except(definition.SupportedProperties)) Add(issues, "unsupported_property", $"componentsLayout.components[{component.Id}].properties.{property}", "Component property is not registered.");
                if (!definition.AllowsChildren && component.ChildIds.Count != 0) Add(issues, "children_not_allowed", $"componentsLayout.components[{component.Id}].childIds", "Component type cannot contain children.");
            }
            if (features is not null) References(issues, [component.ScreenId], screens, $"componentsLayout.components[{component.Id}].screenId");
            References(issues, component.ChildIds, components.Keys.ToHashSet(), $"componentsLayout.components[{component.Id}].childIds");
            if (component.ParentId is not null) References(issues, [component.ParentId], components.Keys.ToHashSet(), $"componentsLayout.components[{component.Id}].parentId");
            if (component.ParentId is { } parentId && components.TryGetValue(parentId, out var parent) &&
                (!parent.ChildIds.Contains(component.Id) || parent.ScreenId != component.ScreenId))
                Add(issues, "inconsistent_hierarchy", $"componentsLayout.components[{component.Id}].parentId", "Parent and child references must be reciprocal and belong to the same screen.");
            if (component.Protected && component.EditableProperties.Count != 0) Add(issues, "protected_editable", $"componentsLayout.components[{component.Id}]", "Protected components cannot expose editable properties.");
            if (component.Layout.Columns <= 0) Add(issues, "invalid_layout", $"componentsLayout.components[{component.Id}].layout.columns", "Layout columns must be positive.");
            if (theme is not null && !theme.Spacing.ContainsKey(component.Layout.GapToken))
                Add(issues, "unknown_spacing", $"componentsLayout.components[{component.Id}].layout.gapToken", "Layout references an unknown spacing token.");
            foreach (var breakpoint in component.ResponsiveLayouts.Keys)
                if (theme is not null && !theme.Breakpoints.Any(x => x.Name == breakpoint)) Add(issues, "unknown_breakpoint", $"componentsLayout.components[{component.Id}].responsiveLayouts.{breakpoint}", "Responsive layout references an unknown breakpoint.");
        }
        foreach (var component in value.Components)
            if (HasCycle(component.Id, component.Id, components, [])) Add(issues, "component_cycle", $"componentsLayout.components[{component.Id}]", "Component hierarchy contains a cycle.");
    }

    private static void ValidateInteractions(InteractionsStatesStep value, ComponentsLayoutStep? components,
        NavigationStep? navigation, FeaturesScreensStep? features, List<BuilderValidationIssue> issues)
    {
        Unique(issues, value.FormFields.Select(x => x.Id), "interactionsStates.formFields");
        Unique(issues, value.Actions.Select(x => x.Id), "interactionsStates.actions");
        Unique(issues, value.States.Select(x => x.Id), "interactionsStates.states");
        Unique(issues, value.Transitions.Select(x => x.Id), "interactionsStates.transitions");
        var componentIds = components?.Components.Select(x => x.Id).ToHashSet() ?? [];
        var routeIds = navigation?.Routes.Select(x => x.Id).ToHashSet() ?? [];
        var stateIds = value.States.Select(x => x.Id).ToHashSet();
        var actionIds = value.Actions.Select(x => x.Id).ToHashSet();
        var screenIds = features?.Screens.Select(x => x.Id).ToHashSet() ?? [];
        foreach (var field in value.FormFields) if (components is not null) References(issues, [field.ComponentId], componentIds, $"interactionsStates.formFields[{field.Id}].componentId");
        foreach (var action in value.Actions)
        {
            if (components is not null) References(issues, [action.ComponentId], componentIds, $"interactionsStates.actions[{action.Id}].componentId");
            if (navigation is not null && action.TargetRouteId is not null) References(issues, [action.TargetRouteId], routeIds, $"interactionsStates.actions[{action.Id}].targetRouteId");
        }
        foreach (var state in value.States)
        {
            if (!StateKinds.Contains(state.Kind)) Add(issues, "unsupported_state", $"interactionsStates.states[{state.Id}].kind", "Unsupported UI state kind.");
            if (features is not null) References(issues, [state.ScreenId], screenIds, $"interactionsStates.states[{state.Id}].screenId");
            if (components is not null) References(issues, state.VisibleComponentIds, componentIds, $"interactionsStates.states[{state.Id}].visibleComponentIds");
        }
        foreach (var transition in value.Transitions)
        {
            References(issues, [transition.FromStateId, transition.ToStateId], stateIds, $"interactionsStates.transitions[{transition.Id}]");
            References(issues, [transition.ActionId], actionIds, $"interactionsStates.transitions[{transition.Id}].actionId");
        }
    }

    private static void ValidateWorkspace(AiDesignWorkspaceStep value, ComponentsLayoutStep? components,
        FeaturesScreensStep? features, List<BuilderValidationIssue> issues)
    {
        if (value.BlueprintSchemaVersion != "1.0") Add(issues, "unsupported_blueprint", "aiDesignWorkspace.blueprintSchemaVersion", "Only Blueprint 1.0 is supported.");
        if (value.AutosaveEnabled && value.AutosaveSeconds is < 5 or > 3600) Add(issues, "invalid_autosave", "aiDesignWorkspace.autosaveSeconds", "Autosave interval must be from 5 to 3600 seconds.");
        if (components is not null)
        {
            var ids = components.Components.Select(x => x.Id).ToHashSet();
            References(issues, value.ProtectedComponentIds, ids, "aiDesignWorkspace.protectedComponentIds");
            References(issues, value.EditableProperties.Keys, ids, "aiDesignWorkspace.editableProperties");
            foreach (var id in value.ProtectedComponentIds.Intersect(value.EditableProperties.Keys)) Add(issues, "protected_editable", $"aiDesignWorkspace.editableProperties.{id}", "Protected components cannot be editable.");
        }
        if (features is not null) References(issues, value.RequirementTraceIds,
            features.Requirements.Select(x => x.Id).ToHashSet(), "aiDesignWorkspace.requirementTraceIds");
    }

    private static void ValidateReview(ReviewPreviewStep value, ComponentsLayoutStep? components,
        InteractionsStatesStep? interactions, NavigationStep? navigation, FeaturesScreensStep? features,
        List<BuilderValidationIssue> issues)
    {
        Unique(issues, value.Decisions.Select(x => x.Id), "reviewPreview.decisions");
        if (value.RequirementCount < 0 || value.CoveredRequirementCount < 0 || value.CoveredRequirementCount > value.RequirementCount)
            Add(issues, "invalid_coverage", "reviewPreview", "Requirement coverage counts are invalid.");
        if (features is not null && value.RequirementCount != features.Requirements.Count)
            Add(issues, "coverage_mismatch", "reviewPreview.requirementCount", "Requirement count does not match the catalogue.");
        if (components is not null)
        {
            var componentIds = components.Components.Select(x => x.Id).ToHashSet();
            foreach (var screen in value.Screens) References(issues, screen.ComponentIds, componentIds, $"reviewPreview.screens[{screen.ScreenId}].componentIds");
        }
        if (interactions is not null)
        {
            var stateIds = interactions.States.Select(x => x.Id).ToHashSet();
            foreach (var screen in value.Screens) References(issues, screen.StateIds, stateIds, $"reviewPreview.screens[{screen.ScreenId}].stateIds");
        }
        if (navigation is not null) References(issues, value.Navigation.Select(x => x.RouteId),
            navigation.Routes.Select(x => x.Id).ToHashSet(), "reviewPreview.navigation");
        if (value.ReadyForApproval && value.CoveredRequirementCount != value.RequirementCount)
            Add(issues, "incomplete_coverage", "reviewPreview.readyForApproval", "Approval readiness requires complete requirement coverage.");
    }

    private static bool HasCycle(string origin, string current,
        IReadOnlyDictionary<string, BuilderComponent> components, HashSet<string> visited)
    {
        if (!visited.Add(current)) return current == origin;
        if (!components.TryGetValue(current, out var component)) return false;
        foreach (var child in component.ChildIds)
            if (child == origin || HasCycle(origin, child, components, new HashSet<string>(visited))) return true;
        return false;
    }

    private static void References(List<BuilderValidationIssue> issues, IEnumerable<string> values,
        HashSet<string> valid, string path)
    {
        foreach (var value in values.Where(x => Blank(x) || !valid.Contains(x)))
            Add(issues, "broken_reference", path, $"Unknown reference '{value}'.");
    }

    private static void Unique(List<BuilderValidationIssue> issues, IEnumerable<string> values, string path)
    {
        var items = values.ToArray();
        if (items.Any(Blank) || items.Distinct(StringComparer.Ordinal).Count() != items.Length)
            Add(issues, "invalid_id", path, "IDs must be non-empty and unique.");
    }

    private static void Required(List<BuilderValidationIssue> issues, string value, string path)
    {
        if (Blank(value)) Add(issues, "required", path, "A value is required.");
    }

    private static void NonEmpty(List<BuilderValidationIssue> issues, IReadOnlyCollection<string> value, string path)
    {
        if (value.Count == 0 || value.Any(Blank)) Add(issues, "required", path, "At least one non-empty value is required.");
    }

    private static void Missing(List<BuilderValidationIssue> issues, string path) =>
        Add(issues, "missing_step", path, "Builder step is required for completion.");
    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    private static void Add(List<BuilderValidationIssue> issues, string code, string path, string message) =>
        issues.Add(new(code, path, message));

    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex HexColor();
}
