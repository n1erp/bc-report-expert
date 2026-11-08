using ReportExpert.Modules.Copilot.Models;
using ReportExpert.Modules.Copilot.Services;

namespace ReportExpert.Modules.Copilot.ViewModels;

/// <summary>A provider in the Rex picker, with its models and whether it can be used.</summary>
public sealed class ProviderChoiceViewModel
{
    public required CopilotProvider Provider { get; init; }

    public string DisplayName => CopilotProviderDefaults.DisplayName(Provider);

    /// <summary>A key is set and has not been rejected.</summary>
    public required bool IsUsable { get; init; }

    /// <summary>The provider Rex sends to now.</summary>
    public required bool IsActive { get; init; }

    /// <summary>Why it cannot be picked ("No API key", "Key rejected"); empty when usable.</summary>
    public string Status { get; init; } = string.Empty;

    public required IReadOnlyList<ModelChoiceViewModel> Models { get; init; }
}

/// <summary>One model under a provider in the Rex picker.</summary>
public sealed class ModelChoiceViewModel
{
    public required CopilotProvider Provider { get; init; }

    public required AiModelOption Option { get; init; }

    /// <summary>The provider is active and this is its model.</summary>
    public required bool IsSelected { get; init; }

    /// <summary>The model saved for this provider (used when Rex switches to it).</summary>
    public bool IsProviderModel { get; init; }

    public string DisplayName => Option.DisplayName;

    public string ModelId => Option.ModelId;

    public string BestFor => Option.BestFor;
}
