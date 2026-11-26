using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Reqviora.Application.Features.GuidedBuilder;

namespace Reqviora.Application.Features.SecondBrain;

public sealed record SecondBrainPackage(string ManifestJson, byte[] Archive, string ArchiveSha256);

public sealed record SecondBrainManifestFile(string Path, long SizeBytes, string Sha256);

public sealed record SecondBrainManifest(
    string Format, string SchemaVersion, string GeneratorVersion, Guid ProjectId, Guid RevisionId,
    string ApprovalStatus, DateTimeOffset CreatedOn, int ReferenceCount, IReadOnlyList<SecondBrainManifestFile> Files);

public static class SecondBrainPackageBuilder
{
    public const string GeneratorVersion = "1.1.0";
    private const string ManifestFormat = "reqviora.second-brain-manifest";
    private const string ManifestPath = "manifest.json";

    private static readonly string[] ReferenceNames =
    [
        "README.md", "project-overview.md", "goals-and-success-criteria.md", "audience-and-roles.md",
        "scope-and-constraints.md", "feature-catalog.md", "business-rules-and-acceptance.md", "screen-specifications.md",
        "feature-screen-mapping.md", "navigation-and-journeys.md", "design-tokens.md", "components-and-layout.md",
        "interactions-and-states.md", "accessibility-requirements.md", "decision-register.md", "open-questions.md",
        "validation-report.md", "implementation-checklist.md", "software-requirements-specification.md",
        "requirement-traceability-matrix.md", "developer-handoff.md", "universal-blueprint.json",
        "asset-manifest.json", "project.json"
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static SecondBrainPackage Build(ApprovedProjectSnapshot snapshot)
    {
        var files = Render(snapshot);
        var manifest = new SecondBrainManifest(
            ManifestFormat, "1.0.0", GeneratorVersion, snapshot.ProjectId, snapshot.RevisionId, "approved",
            snapshot.ApprovedAt, ReferenceNames.Length,
            ReferenceNames.Select(path => new SecondBrainManifestFile(path, files[path].Length, Hash(files[path]))).ToArray());
        var manifestJson = JsonSerializer.Serialize(manifest, Json);
        using var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in ReferenceNames)
                WriteEntry(archive, name, files[name]);

            WriteEntry(archive, ManifestPath, Encoding.UTF8.GetBytes(manifestJson));
        }

        var bytes = stream.ToArray();
        return new(manifestJson, bytes, Hash(bytes));
    }

    public static bool Verify(SecondBrainPackage package, Guid projectId, Guid revisionId)
    {
        if (!string.Equals(Hash(package.Archive), package.ArchiveSha256, StringComparison.Ordinal))
            return false;

        try
        {
            var manifest = JsonSerializer.Deserialize<SecondBrainManifest>(package.ManifestJson, Json);
            if (manifest is null || manifest.Format != ManifestFormat || manifest.SchemaVersion != "1.0.0" ||
                manifest.GeneratorVersion != GeneratorVersion || manifest.ApprovalStatus != "approved" ||
                manifest.ProjectId != projectId || manifest.RevisionId != revisionId ||
                manifest.ReferenceCount != ReferenceNames.Length || manifest.Files is null ||
                !manifest.Files.Select(file => file?.Path).SequenceEqual(ReferenceNames, StringComparer.Ordinal))
            {
                return false;
            }

            using var stream = new MemoryStream(package.Archive, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (!archive.Entries.Select(entry => entry.FullName).SequenceEqual(
                ReferenceNames.Append(ManifestPath), StringComparer.Ordinal))
            {
                return false;
            }

            using var embeddedManifest = archive.Entries[^1].Open();
            var manifestBytes = Encoding.UTF8.GetBytes(package.ManifestJson);
            if (archive.Entries[^1].Length != manifestBytes.Length ||
                !string.Equals(Hash(manifestBytes), Hash(embeddedManifest), StringComparison.Ordinal))
            {
                return false;
            }

            for (var i = 0; i < manifest.Files.Count; i++)
            {
                var entry = archive.Entries[i];
                var expected = manifest.Files[i];
                using var content = entry.Open();

                if (entry.Length != expected.SizeBytes ||
                    !string.Equals(Hash(content), expected.Sha256, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private static Dictionary<string, byte[]> Render(ApprovedProjectSnapshot snapshot)
    {
        using var draft = JsonDocument.Parse(snapshot.DraftJson);
        var notes = draft.RootElement.TryGetProperty("secondBrain", out var secondBrain) &&
            secondBrain.ValueKind == JsonValueKind.Object ? secondBrain : default;
        var builder = draft.RootElement.TryGetProperty("guidedBuilder", out var builderElement)
            ? builderElement.Deserialize<GuidedBuilderDocument>(Json)
            : null;
        var blueprint = snapshot.Blueprint;
        var features = blueprint.Features.ToDictionary(feature => feature.Id, StringComparer.Ordinal);

        string Notes(string section)
        {
            if (notes.ValueKind == JsonValueKind.Object && notes.TryGetProperty(section, out var value) &&
                value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }

            return "Author input pending.";
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["README.md"] = Markdown("Second Brain", $"Project {snapshot.ProjectName}",
                $"Approved revision {snapshot.RevisionId}", Bullets(ReferenceNames.Skip(1).Select(name => $"[{name}]({name})"))),
            ["project-overview.md"] = Markdown("Project overview", snapshot.ProjectName, snapshot.ProjectDescription,
                $"Project ID {snapshot.ProjectId}", $"Draft version {snapshot.DraftVersion}"),
            ["goals-and-success-criteria.md"] = Markdown("Goals and success criteria",
                builder?.IdentityAudience is { } identity
                    ? MarkdownSections("Goals", identity.Goals, "Success criteria", identity.SuccessCriteria)
                    : Notes("goalsAndSuccessCriteria"),
                Bullets(blueprint.Requirements.Where(r => r.Status == "confirmed").Select(r => $"{r.Id} {r.Text}"))),
            ["audience-and-roles.md"] = Markdown("Audience and roles", $"Project owner {snapshot.ProjectOwnerId}",
                builder?.IdentityAudience is { } audience
                    ? MarkdownSections("Audience", audience.TargetAudience,
                        "Roles", audience.Roles.Select(role => $"{role.Id} {role.Name}: {role.Description}"))
                    : Notes("audienceAndRoles")),
            ["scope-and-constraints.md"] = Markdown("Scope and constraints",
                $"Target platforms {string.Join(", ", blueprint.TargetPlatforms)}",
                builder?.IdentityAudience is { } scope
                    ? MarkdownSections("Scope", [scope.Scope], "Constraints", scope.Constraints, "Assumptions", scope.Assumptions)
                    : Notes("scopeAndConstraints")),
            ["feature-catalog.md"] = Markdown("Feature catalog", Bullets(blueprint.Features.Select(
                feature => $"{feature.Id} {feature.Name} | Requirements {string.Join(", ", feature.RequirementIds)}"))),
            ["business-rules-and-acceptance.md"] = Markdown("Business rules and acceptance", Notes("businessRulesAndAcceptance"),
                Bullets(blueprint.Requirements.Select(r => $"{r.Id} [{r.Status}] {r.Text} | Sources " +
                    string.Join(", ", r.Evidence.Select(evidence => $"{evidence.SourceId} {evidence.Locator}"))))),
            ["screen-specifications.md"] = Markdown("Screen specifications", Bullets(blueprint.Screens.Select(
                screen => $"{screen.Id} {screen.Name} | Components {string.Join(", ", screen.ComponentIds)}"))),
            ["feature-screen-mapping.md"] = Markdown("Feature-screen mapping", Bullets(blueprint.Screens.SelectMany(
                screen => screen.FeatureIds.Select(featureId => $"{screen.Id} → {featureId} → " +
                    string.Join(", ", features[featureId].RequirementIds))))),
            ["navigation-and-journeys.md"] = Markdown("Navigation and journeys", Bullets(blueprint.Navigation.Select(
                edge => $"{edge.Id} {edge.SourceScreenId} → {edge.TargetScreenId} via {edge.ActionId}"))),
            ["design-tokens.md"] = Markdown("Design tokens",
                builder?.ThemeBrand is { } theme ? JsonBlock(theme) : Notes("designTokens")),
            ["components-and-layout.md"] = Markdown("Components and layout",
                builder?.ComponentsLayout is { } componentLayout ? JsonBlock(componentLayout) : Notes("componentsAndLayout"),
                Bullets(blueprint.Components.Select(component => $"{component.Id} {component.Type} on {component.ScreenId}"))),
            ["interactions-and-states.md"] = Markdown("Interactions and states",
                builder?.InteractionsStates is { } states ? JsonBlock(states) : Notes("interactionsAndStates"),
                Bullets(blueprint.Actions.Select(action => $"{action.Id} {action.Kind} on {action.ComponentId}" +
                    (action.NavigationId is null ? "" : $" via {action.NavigationId}")))),
            ["accessibility-requirements.md"] = Markdown("Accessibility requirements",
                builder?.ThemeBrand is { } accessible ? JsonBlock(accessible.Accessibility) : Notes("accessibilityRequirements")),
            ["decision-register.md"] = Markdown("Decision register", $"Approved by {snapshot.ApprovedBy} at {snapshot.ApprovedAt:O}",
                snapshot.BasedOnRevisionId is { } parent ? $"Based on approved revision {parent}" : "First approval lineage",
                builder?.ReviewPreview is { } review
                    ? Bullets(review.Decisions.Select(decision => $"{decision.Id} [{decision.Decision}] {decision.SubjectId}: {decision.Comment}"))
                    : "",
                Notes("decisionRegister")),
            ["open-questions.md"] = Markdown("Open questions", Notes("openQuestions")),
            ["validation-report.md"] = Markdown("Validation report",
                $"Validation run {snapshot.ValidationRunId} passed at {snapshot.ValidatedAt:O}.",
                $"Validated draft version {snapshot.DraftVersion}."),
            ["implementation-checklist.md"] = Markdown("Implementation checklist", Notes("implementationChecklist"),
                Bullets(blueprint.Features.Select(feature => $"[ ] Implement {feature.Id} {feature.Name}"))),
            ["software-requirements-specification.md"] = Markdown("Software requirements specification",
                snapshot.ProjectDescription,
                "## Functional requirements",
                Bullets(blueprint.Requirements.Select(requirement =>
                    $"{requirement.Id} [{requirement.Status}] {requirement.Text}")),
                "## Target platforms", Bullets(blueprint.TargetPlatforms)),
            ["requirement-traceability-matrix.md"] = Markdown("Requirement traceability matrix",
                Bullets(blueprint.Requirements.Select(requirement =>
                {
                    var mappedFeatures = blueprint.Features.Where(feature => feature.RequirementIds.Contains(requirement.Id)).ToArray();
                    var mappedScreens = blueprint.Screens.Where(screen =>
                        screen.FeatureIds.Any(featureId => mappedFeatures.Any(feature => feature.Id == featureId)));
                    return $"{requirement.Id} → {string.Join(", ", mappedFeatures.Select(feature => feature.Id))} → " +
                        string.Join(", ", mappedScreens.Select(screen => screen.Id));
                }))),
            ["developer-handoff.md"] = Markdown("Developer handoff",
                $"Universal Blueprint schema {blueprint.SchemaVersion}",
                $"Guided Builder schema {builder?.SchemaVersion ?? "not supplied"}",
                $"Target platforms {string.Join(", ", blueprint.TargetPlatforms)}",
                $"Screens {blueprint.Screens.Count}; components {blueprint.Components.Count}; actions {blueprint.Actions.Count}",
                "Use universal-blueprint.json as the machine-readable UI contract and project.json as the immutable approved snapshot."),
            ["universal-blueprint.json"] = JsonSerializer.Serialize(blueprint, Json) + "\n",
            ["asset-manifest.json"] = JsonSerializer.Serialize(
                builder?.ThemeBrand?.BrandAssets ?? [], Json) + "\n",
            ["project.json"] = JsonSerializer.Serialize(snapshot, Json) + "\n"
        };

        return files.ToDictionary(file => file.Key, file => Encoding.UTF8.GetBytes(file.Value), StringComparer.Ordinal);
    }

    private static string Markdown(string title, params string[] sections) =>
        $"# {title}\n\n" + string.Join("\n\n", sections.Where(section => !string.IsNullOrWhiteSpace(section))) + "\n";

    private static string Bullets(IEnumerable<string> lines) =>
        string.Join("\n", lines.Select(line => $"- {line.ReplaceLineEndings(" ")}"));

    private static string MarkdownSections(string firstTitle, IEnumerable<string> firstValues,
        string secondTitle, IEnumerable<string> secondValues, string? thirdTitle = null,
        IEnumerable<string>? thirdValues = null) =>
        $"## {firstTitle}\n\n{Bullets(firstValues)}\n\n## {secondTitle}\n\n{Bullets(secondValues)}" +
        (thirdTitle is null ? "" : $"\n\n## {thirdTitle}\n\n{Bullets(thirdValues ?? [])}");

    private static string JsonBlock<T>(T value) =>
        $"```json\n{JsonSerializer.Serialize(value, Json)}\n```";

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Hash(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var content = entry.Open();
        content.Write(bytes);
    }
}
