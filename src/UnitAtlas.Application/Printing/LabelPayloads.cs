using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnitAtlas.Contracts;

namespace UnitAtlas.Application.Printing;

public static class LabelPayloads
{
    public static readonly string[] IdentifierModes = ["INTERNAL", "GS1"];
    public static readonly string[] EntityTypes = ["UNIT", "LOGISTIC_UNIT"];
    public static readonly string[] AttemptStatuses = ["DISPATCHED", "PRINTED", "FAILED"];

    public static LabelPayload Internal(string entityType, string code) =>
        new($"unitatlas:{entityType.ToLowerInvariant()}:{code}", code);

    public static bool TryGs1Unit(string gtin, string lot, string serial, string companyPrefix, out LabelPayload? payload)
    {
        payload = null;
        if (!ValidGs1Prefix(companyPrefix) || !ValidGtin(gtin)
            || !gtin[1..].StartsWith(companyPrefix, StringComparison.Ordinal)
            || InvalidVariable(lot) || InvalidVariable(serial)) return false;
        payload = new LabelPayload($"01{gtin}10{lot}\u001d21{serial}", $"(01){gtin}(10){lot}(21){serial}");
        return true;
    }

    public static bool TryGs1Logistic(string? sscc, string companyPrefix, out LabelPayload? payload)
    {
        payload = null;
        if (sscc is null || !ValidGs1Prefix(companyPrefix) || !ValidCheckDigit(sscc, 18)
            || !sscc[1..].StartsWith(companyPrefix, StringComparison.Ordinal)) return false;
        payload = new LabelPayload($"00{sscc}", $"(00){sscc}");
        return true;
    }

    public static bool ValidGs1Prefix(string? value) =>
        value is { Length: >= 6 and <= 12 } && value.All(char.IsDigit);

    public static string RequestHash(PrintJobRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.TemplateId,
            request.ProfileId,
            request.PrinterId,
            EntityType = request.EntityType?.Trim().ToUpperInvariant(),
            Code = request.Code?.Trim(),
            Copies = Math.Clamp(request.Copies, 1, 100)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool ValidGtin(string value) => value.Length == 14 && ValidCheckDigit(value, 14);

    private static bool ValidCheckDigit(string value, int length)
    {
        if (value.Length != length || !value.All(char.IsDigit)) return false;
        var sum = value[..^1].Select((digit, index) => (digit - '0') * ((length - index) % 2 == 0 ? 3 : 1)).Sum();
        return (10 - sum % 10) % 10 == value[^1] - '0';
    }

    private static bool InvalidVariable(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains('\u001d') || value.Length > 20;
}

public sealed record LabelPayload(string Encoded, string HumanReadable);
