using System.Collections.Generic;

namespace OpenBI.Patching;

public sealed class OpenBIPatchResult
{
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>Patched artifact bytes. Best-effort: contains partial result if some changes failed.</summary>
    public required byte[] Artifact { get; init; }

    /// <summary>Blocking errors collected during patch. Empty on full success. Client should not proceed without addressing these.</summary>
    public IReadOnlyList<OpenBIPatchError> Errors { get; init; } = Array.Empty<OpenBIPatchError>();

    /// <summary>Non-blocking warnings collected during patch. Changes that were skipped due to data drift or unsupported operations. Artifact is still deliverable.</summary>
    public IReadOnlyList<OpenBIPatchError> Warnings { get; init; } = Array.Empty<OpenBIPatchError>();
}
