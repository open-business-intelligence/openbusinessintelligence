using System;

namespace OpenBI.Patching;

public sealed class OpenBIPatchError
{
    public required OpenBIEntity   Entity          { get; init; }
    public string?                 Id              { get; init; }
    public string?                 Property        { get; init; }
    public required OpenBIChangeOp Op              { get; init; }
    public required string         Message         { get; init; }
    public Exception?              InnerException  { get; init; }
}
