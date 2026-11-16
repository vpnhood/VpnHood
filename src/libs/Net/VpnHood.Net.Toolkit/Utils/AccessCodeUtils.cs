using System.Text;
using System.Text.RegularExpressions;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides access codes: 20 characters, the version digit 1, a check digit, and 18 random characters.
/// </summary>
public static class AccessCodeUtils
{
    /// <summary>Builds an access code from its random part, which it neither checks nor makes.</summary>
    /// <param name="random">The random part, 18 ASCII letters or digits for a valid code.</param>
    /// <returns>The access code: <c>1</c>, the check digit, then the random part.</returns>
    public static string Build(string random)
    {
        var checksum = CalculateChecksum(random);
        const int version = 1;
        var accessCode = $"{version}{checksum}{random}";
        return accessCode;
    }

    /// <summary>Checks an access code, as <see cref="Validate"/> does, without throwing.</summary>
    /// <param name="accessCode">The access code, as typed.</param>
    /// <returns>The code without its separators, or <see langword="null"/> when it is not valid.</returns>
    public static string? TryValidate(string accessCode)
    {
        try {
            return Validate(accessCode);
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// Checks an access code: what is not an ASCII letter or digit, such as a dash, is dropped, and the rest must be version 1's
    /// 20 characters with a matching check digit.
    /// </summary>
    /// <param name="accessCode">The access code, as typed.</param>
    /// <returns>The code without its separators.</returns>
    /// <exception cref="ArgumentException">The code is empty, of another version or length, or its check digit does not match.</exception>
    public static string Validate(string accessCode)
    {
        accessCode = Regex.Replace(accessCode, "[^a-zA-Z0-9]", "").Trim();

        if (string.IsNullOrEmpty(accessCode))
            throw new ArgumentException("Access Code is empty.");

        if (!int.TryParse(accessCode[0].ToString(), out var version) || version != 1)
            throw new ArgumentException("Unrecognized Access Code. First digit should be 1.");

        // version detected. Version 1 has 20 digits
        if (accessCode.Length != 20)
            throw new ArgumentException("Access code must have 20 digit.");

        // get checksum
        if (!int.TryParse(accessCode[1].ToString(), out var checksum))
            throw new ArgumentException("Invalid Access Code.", nameof(accessCode));

        // calculate checksum
        var random = accessCode[2..20];
        var calculatedChecksum = CalculateChecksum(random);
        if (calculatedChecksum != checksum)
            throw new ArgumentException("Invalid Access Code.", nameof(accessCode));

        return accessCode;
    }


    private static int CalculateChecksum(string input)
    {
        // Sum the ASCII values of each character in the string
        var asciiSum = input.Sum(c => c);

        // Reduce to a single digit by summing digits repeatedly
        while (asciiSum >= 10) {
            asciiSum = asciiSum.ToString().Sum(c => c - '0');
        }

        return asciiSum;
    }

    /// <summary>Formats a code for reading, with a dash after every four characters.</summary>
    /// <param name="value">The code, or <see langword="null"/>.</param>
    /// <returns>The formatted code, or empty.</returns>
    public static string Format(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // for all digits, add a dash after every 4 digits
        var formattedValue = new StringBuilder();
        for (var i = 0; i < value.Length; i++) {
            if (i > 0 && i % 4 == 0)
                formattedValue.Append('-');
            formattedValue.Append(value[i]);
        }

        return formattedValue.ToString();
    }

    /// <summary>Hides a valid access code but its last four characters, for a log or a screen.</summary>
    /// <param name="accessCode">The access code, or <see langword="null"/>.</param>
    /// <returns>The code with all but its last four characters as <c>*</c>, or <see langword="null"/> when it is not valid.</returns>
    public static string? Redact(string? accessCode)
    {
        accessCode = TryValidate(accessCode ?? "");
        if (accessCode == null)
            return null;

        if (accessCode.Length <= 4)
            return "***";

        // replace all but last 4 characters with '*'
        return new string('*', accessCode.Length - 4) + accessCode[^4..];
    }
}