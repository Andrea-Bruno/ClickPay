using System;
using System.Globalization;

namespace ClickPay.Shared.Pages;

/// <summary>
/// Shared helper routines for locale-aware numeric formatting used by wallet pages.
/// </summary>
internal static class WalletConversionHelpers
{
    public static string GetCryptoPlaceholder(int decimals)
    {
        var normalizedDecimals = Math.Clamp(decimals, 2, 8);
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return $"0{separator}{new string('0', Math.Min(normalizedDecimals, 4))}";
    }

    public static string GetFiatPlaceholder()
    {
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return $"0{separator}00";
    }

    public static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0m;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var culture = CultureInfo.CurrentCulture;
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        var alternateSeparator = decimalSeparator == "," ? "." : ",";

        if (trimmed.Contains(alternateSeparator, StringComparison.Ordinal) && !trimmed.Contains(decimalSeparator, StringComparison.Ordinal))
        {
            trimmed = trimmed.Replace(alternateSeparator, decimalSeparator, StringComparison.Ordinal);
        }

        if (decimal.TryParse(trimmed, NumberStyles.Number, culture, out result))
        {
            return true;
        }

        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    public static string FormatCrypto(decimal amount, int decimals)
    {
        var format = decimals <= 0 ? "0" : $"0.{new string('#', decimals)}";
        return amount.ToString(format, CultureInfo.CurrentCulture);
    }

    public static string FormatFiat(decimal amount)
        => amount.ToString("0.00######", CultureInfo.CurrentCulture);
}