using WMS.Models;

namespace WMS.Services;

internal static class VoucherQuantityPrecision
{
    internal const decimal EqualityTolerance = 0.0001m;

    internal static decimal RoundTransaction(decimal value)
        => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    internal static decimal RoundConversion(decimal value)
        => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    internal static decimal RoundBase(decimal value)
        => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    internal static BackorderLineQuantity ProjectBackorderLine(VoucherDetail source, decimal shortBaseQty)
    {
        var baseQty = RoundBase(shortBaseQty);
        if (baseQty <= 0)
        {
            throw new BusinessRuleException(
                "Số lượng còn thiếu không đủ độ chính xác để tạo phiếu bổ sung.",
                "BACKORDER_QTY_INVALID",
                "VoucherDetail");
        }

        var conversionRate = source.TransactionQty != 0
            ? Math.Abs(source.BaseQty) / Math.Abs(source.TransactionQty)
            : source.ConversionRate;
        conversionRate = RoundConversion(conversionRate);
        if (conversionRate <= 0)
        {
            throw new BusinessRuleException(
                "Quy đổi đơn vị của dòng hàng không hợp lệ để tạo phiếu bổ sung.",
                "BACKORDER_UOM_INVALID",
                "VoucherDetail");
        }

        var transactionQty = RoundTransaction(baseQty / conversionRate);
        var representedBaseQty = RoundBase(transactionQty * conversionRate);
        var transactionUomId = source.TransactionUomId;
        var packagingUnitId = source.PackagingUnitId;

        // A very small remainder may not be representable in the original transaction UOM.
        // Preserve the exact remaining base quantity by falling back to the item's base UOM.
        if (transactionQty <= 0 || Math.Abs(representedBaseQty - baseQty) > EqualityTolerance)
        {
            var baseUomId = source.Item?.BaseUomId ?? 0;
            if (baseUomId <= 0)
            {
                throw new BusinessRuleException(
                    "Không xác định được đơn vị cơ sở để tạo phiếu bổ sung.",
                    "BACKORDER_BASE_UOM_MISSING",
                    "VoucherDetail");
            }

            transactionQty = baseQty;
            transactionUomId = baseUomId;
            packagingUnitId = null;
            conversionRate = 1m;
        }

        return new BackorderLineQuantity(
            transactionQty,
            transactionUomId,
            packagingUnitId,
            conversionRate,
            baseQty);
    }
}

internal readonly record struct BackorderLineQuantity(
    decimal TransactionQty,
    int TransactionUomId,
    int? PackagingUnitId,
    decimal ConversionRate,
    decimal BaseQty);
