namespace HushNode.Interfaces;

public static class DecimalStringConverter
{
    /// <summary>
    /// Converts a decimal value to a string with the specified number of decimal places.
    /// </summary>
    public static string DecimalToString(decimal value, int precision)
    {
        if (precision < 0)
            throw new ArgumentException("Precision must be a non-negative integer.");

        // Convert to string with the specified number of decimal places
        string valueWithDecimals = value.ToString($"F{precision}", System.Globalization.CultureInfo.InvariantCulture);
        
        // Remove the decimal point to create a continuous string of digits
        return valueWithDecimals.Replace(".", "");
    }

    /// <summary>
    /// Converts a string with the specified number of decimal places back to a decimal value.
    /// </summary>
    public static decimal StringToDecimal(string value, int precision)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty.");
        if (precision < 0)
            throw new ArgumentException("Precision must be a non-negative integer.");

        // Ensure the string has at least the specified number of digits
        if (value.Length < precision)
            throw new ArgumentException($"Value must have at least {precision} digits.");

        // Split the string into integer and fractional parts
        int totalDigits = value.Length;
        int integerPartLength = totalDigits - precision;
        
        string integerPart = integerPartLength > 0 ? value[..integerPartLength] : "0";
        string fractionalPart = integerPartLength > 0 ? value[integerPartLength..] : value;

        // Combine the parts with a decimal point
        string decimalString = $"{integerPart}.{fractionalPart}";

        // Parse to decimal
        return decimal.Parse(decimalString, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Overloads for default precision (9)
    public static string DecimalToString(decimal value) => DecimalToString(value, 9);
    public static decimal StringToDecimal(string value) => StringToDecimal(value, 9);
}

