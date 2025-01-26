using System.Text.RegularExpressions;

namespace VpnHood.Core.Common.Utils;

public static class AccessCodeUtils
{

    public static string Build(string random)
    {
        var checksum = CalculateChecksum(random);
        const int version = 1;
        var accessCode = $"{version}{checksum}{random}";
        return accessCode;
    }
    
    public static string Validate(string accessCode)
    {
        accessCode = Regex.Replace(accessCode, "[^a-zA-Z0-9]", "").Trim();

        if (string.IsNullOrEmpty(accessCode))
            throw new FormatException("Access Code is empty.");

        if (!int.TryParse(accessCode[0].ToString(), out var version) || version != 1)
            throw new FormatException("Unrecognized Access Code. First digit should be 1.");

        // version detected. Version 1 has 20 digits
        if (accessCode.Length != 20)
            throw new FormatException("Access code must have 20 digit.");

        // get checksum
        if (!int.TryParse(accessCode[1].ToString(), out var checksum))
            throw new FormatException("Invalid Access Code.");

        // calculate checksum
        var random = accessCode[2..20];
        var calculatedChecksum = CalculateChecksum(random);
        if (calculatedChecksum != checksum)
            throw new FormatException("Invalid Access Code.");

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
}