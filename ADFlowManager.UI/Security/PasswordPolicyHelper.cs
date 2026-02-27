using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ADFlowManager.UI.Security;

public static class PasswordPolicyHelper
{
    public const string Easy = "Easy";
    public const string Standard = "Standard";
    public const string Strong = "Strong";

    private static readonly char[] SpecialChars = ['@', '#', '!', '$', '%', '&', '*'];
    private static readonly string[] EasyWords =
    [
        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot",
        "Galaxy", "Hunter", "Indigo", "Jungle", "Lotus", "Mango",
        "Omega", "Phoenix", "Quartz", "Rocket", "Shadow", "Titan"
    ];

    public sealed record PasswordPolicyRules(
        int MinLength,
        bool RequireLower,
        bool RequireUpper,
        bool RequireDigit,
        bool RequireSpecial,
        bool BlockObviousPattern,
        string Label);

    public static PasswordPolicyRules GetRules(string? policy)
    {
        return policy switch
        {
            Easy => new PasswordPolicyRules(8, false, false, true, true, false, "Easy"),
            Standard => new PasswordPolicyRules(12, true, true, true, true, false, "Standard"),
            _ => new PasswordPolicyRules(14, true, true, true, true, true, "Strong")
        };
    }

    public static bool IsCompliant(string? password, string? policy, out string reason)
    {
        reason = string.Empty;
        var rules = GetRules(policy);

        if (string.IsNullOrWhiteSpace(password))
        {
            reason = "Password is required.";
            return false;
        }

        if (password.Length < rules.MinLength)
        {
            reason = $"Password must be at least {rules.MinLength} characters.";
            return false;
        }

        if (rules.RequireLower && !Regex.IsMatch(password, "[a-z]"))
        {
            reason = "Password must include a lowercase letter.";
            return false;
        }

        if (rules.RequireUpper && !Regex.IsMatch(password, "[A-Z]"))
        {
            reason = "Password must include an uppercase letter.";
            return false;
        }

        if (rules.RequireDigit && !Regex.IsMatch(password, "\\d"))
        {
            reason = "Password must include a digit.";
            return false;
        }

        if (rules.RequireSpecial && !Regex.IsMatch(password, "[^a-zA-Z0-9]"))
        {
            reason = "Password must include a special character.";
            return false;
        }

        if (rules.BlockObviousPattern && IsObviousPattern(password))
        {
            reason = "Password pattern is too predictable.";
            return false;
        }

        return true;
    }

    public static string GeneratePassword(string? policy)
    {
        return policy == Easy ? GenerateEasyPassword() : GenerateComplexPassword(GetRules(policy).MinLength);
    }

    private static string GenerateEasyPassword()
    {
        var word = EasyWords[RandomNumberGenerator.GetInt32(EasyWords.Length)];
        var number = RandomNumberGenerator.GetInt32(100, 1000).ToString();
        var special = SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)];
        return $"{word}{number}{special}";
    }

    private static string GenerateComplexPassword(int minLength)
    {
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string special = "@#!$%&*";
        var all = lower + upper + digits + special;

        var length = Math.Max(minLength, 12);
        var password = new char[length];

        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = special[RandomNumberGenerator.GetInt32(special.Length)];

        for (var i = 4; i < length; i++)
            password[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

        for (var i = password.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }

    private static bool IsObviousPattern(string password)
    {
        var lower = password.ToLowerInvariant();
        if (lower.Contains("password") || lower.Contains("azerty") || lower.Contains("qwerty") || lower.Contains("admin"))
            return true;

        return Regex.IsMatch(password, "^[A-Za-z]+\\d{3}[^a-zA-Z0-9]$");
    }
}
