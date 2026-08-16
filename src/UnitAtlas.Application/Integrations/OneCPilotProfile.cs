using System.Text.Json;

namespace UnitAtlas.Application.Integrations;

public static class OneCPilotProfile
{
    public const string Code = "ONEC_UPP_KZ_1_3_HTTP_JSON_V1";
    public const string Name = "1C:Enterprise 8 - Manufacturing Enterprise Management for Kazakhstan, edition 1.3";

    public static bool IsSelected(string settingsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            return document.RootElement.TryGetProperty("profile", out var value)
                && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), Code, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
