using System.Security.Cryptography;
using System.Text;
using UnitAtlas.Contracts;

namespace UnitAtlas.Application.Packaging;

public static class PackagingRules
{
    public static readonly string[] LogisticUnitTypes = ["BOX", "PALLET", "CONTAINER"];
    public static readonly string[] AggregationActions = ["ADD", "DELETE"];

    public static bool IsSupportedType(string? value) =>
        LogisticUnitTypes.Contains(value?.Trim().ToUpperInvariant(), StringComparer.Ordinal);

    public static bool IsSupportedAction(string? value) =>
        AggregationActions.Contains(value?.Trim().ToUpperInvariant(), StringComparer.Ordinal);

    public static bool IsValidSscc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var sscc = new string(value.Where(char.IsDigit).ToArray());
        if (sscc.Length != 18 || sscc.Length != value.Trim().Length) return false;
        var sum = 0;
        for (var i = 0; i < 17; i++)
        {
            var digit = sscc[i] - '0';
            sum += digit * (i % 2 == 0 ? 3 : 1);
        }
        var checkDigit = (10 - sum % 10) % 10;
        return checkDigit == sscc[17] - '0';
    }

    public static string ComputeRequestHash(string parentCode, AggregationRequest request)
    {
        var units = (request.UnitAtlasIds ?? Array.Empty<string>()).Select(x => x.Trim()).Order(StringComparer.Ordinal);
        var logistics = (request.LogisticUnitCodes ?? Array.Empty<string>()).Select(x => x.Trim()).Order(StringComparer.Ordinal);
        var canonical = string.Join('\n',
            parentCode.Trim(),
            request.Action?.Trim().ToUpperInvariant(),
            string.Join(',', units),
            string.Join(',', logistics),
            request.OccurredAt?.ToUniversalTime().ToString("O"),
            request.ReadPointId,
            request.BusinessLocationId,
            request.SourceSystem?.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
