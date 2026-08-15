using System.Security.Cryptography;
using System.Text;
using UnitAtlas.Contracts;

namespace UnitAtlas.Application.Traceability;

public static class EventRequestHash
{
    public static string Compute(string atlasId, EventRequest request)
    {
        var canonical = string.Join('\n',
            atlasId,
            request.EventType?.Trim().ToUpperInvariant(),
            request.Location?.Trim(),
            request.Actor?.Trim(),
            request.OccurredAt?.ToUniversalTime().ToString("O"),
            request.ReadPointId,
            request.BusinessLocationId,
            request.BusinessStep?.Trim(),
            request.Disposition?.Trim(),
            request.SourceSystem?.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
