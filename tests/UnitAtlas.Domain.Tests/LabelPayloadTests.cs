using UnitAtlas.Application.Printing;

namespace UnitAtlas.Domain.Tests;

public sealed class LabelPayloadTests
{
    [Fact]
    public void Gs1_unit_payload_uses_application_identifiers_and_separator()
    {
        Assert.True(LabelPayloads.TryGs1Unit("04871234567890", "LOT-1", "SERIAL-1", "487123", out var payload));
        Assert.Equal("010487123456789010LOT-1\u001d21SERIAL-1", payload!.Encoded);
        Assert.Equal("(01)04871234567890(10)LOT-1(21)SERIAL-1", payload.HumanReadable);
    }

    [Fact]
    public void Gs1_payload_rejects_unlicensed_prefix_and_bad_check_digit()
    {
        Assert.False(LabelPayloads.TryGs1Unit("04871234567891", "LOT-1", "SERIAL-1", "487123", out _));
        Assert.False(LabelPayloads.TryGs1Unit("04871234567890", "LOT-1", "SERIAL-1", "123456", out _));
        Assert.False(LabelPayloads.TryGs1Logistic("123456789012345675", "999999", out _));
    }

    [Fact]
    public void Gs1_logistics_payload_uses_sscc_application_identifier()
    {
        Assert.True(LabelPayloads.TryGs1Logistic("123456789012345675", "234567", out var payload));
        Assert.Equal("00123456789012345675", payload!.Encoded);
        Assert.Equal("(00)123456789012345675", payload.HumanReadable);
    }
}
