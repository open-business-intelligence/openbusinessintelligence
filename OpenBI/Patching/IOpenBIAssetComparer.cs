using System.Collections.Generic;

namespace OpenBI.Patching;

/// <summary>
/// Compares two <see cref="Asset"/> instances and returns the minimal set of
/// <see cref="OpenBIChange"/> objects that transform <paramref name="from"/> into <paramref name="to"/>.
/// <para>
/// All collection comparisons are id-based: items matched by their natural key are compared
/// property-by-property; items only in <paramref name="from"/> produce Remove changes;
/// items only in <paramref name="to"/> produce Add changes.
/// </para>
/// </summary>
public interface IOpenBIAssetComparer
{
    IReadOnlyList<OpenBIChange> Compare(Asset from, Asset to);
}
