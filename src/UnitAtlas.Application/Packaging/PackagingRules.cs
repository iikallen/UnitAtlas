using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    public static string[] NormalizeCodes(IEnumerable<string?>? values) =>
        (values ?? []).Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x))
            .Select(x => x!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

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
        var canonical = JsonSerializer.Serialize(new
        {
            ParentCode = parentCode.Trim(),
            Action = request.Action?.Trim().ToUpperInvariant(),
            UnitAtlasIds = NormalizeCodes(request.UnitAtlasIds),
            LogisticUnitCodes = NormalizeCodes(request.LogisticUnitCodes),
            OccurredAt = request.OccurredAt?.ToUniversalTime().ToString("O"),
            request.ReadPointId,
            request.BusinessLocationId,
            SourceSystem = request.SourceSystem?.Trim()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
