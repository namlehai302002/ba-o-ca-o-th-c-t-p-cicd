using WMS.Models;

namespace WMS.Tests;

public class OperatorMessageTests
{
    [Fact]
    public void SerialCountInsufficient_ShouldExplainRequiredRecordedAndRemainingQuantitiesInVietnamese()
    {
        var error = WmsExceptions.SerialCountInsufficient("AUDIT_TEST_ITEM", 12, 0);

        Assert.Contains("số sê-ri", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12 sản phẩm", error.Message, StringComparison.Ordinal);
        Assert.Contains("0/12", error.Message, StringComparison.Ordinal);
        Assert.Contains("còn thiếu 12", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SerialQuantityMessages_ShouldNotExposeDeveloperEnglish()
    {
        var notInteger = WmsExceptions.SerialNotInteger("AUDIT_TEST_ITEM").Message;
        var mismatch = WmsExceptions.QtyMismatchForSerial("AUDIT_TEST_ITEM", 12, 11.5m).Message;
        var missing = WmsExceptions.SerialMissing("AUDIT_TEST_ITEM", 12, 3).Message;

        Assert.All(new[] { notInteger, mismatch, missing }, message =>
        {
            Assert.Contains("số sê-ri", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("serial", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("expecting", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("got", message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void OutboundBlockingMessages_ShouldUseOperatorFriendlyVietnamese()
    {
        var messages = new[]
        {
            WmsExceptions.NoReservation().Message,
            WmsExceptions.NoPickQty().Message,
            WmsExceptions.QcHoldBlocked("AUDIT_TEST_ITEM").Message,
            WmsExceptions.PartialShipmentNotAllowed().Message,
            WmsExceptions.TransferDestLocationMissingForSerial().Message
        };

        Assert.All(messages, message =>
        {
            Assert.DoesNotContain("reservation", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pick", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("partial", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("serial", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OnHold", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Defect", message, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Contains("giữ chỗ", messages[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("xác nhận lấy", messages[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kiểm soát chất lượng", messages[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("giao từng phần", messages[3], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("số sê-ri", messages[4], StringComparison.OrdinalIgnoreCase);
    }
}
