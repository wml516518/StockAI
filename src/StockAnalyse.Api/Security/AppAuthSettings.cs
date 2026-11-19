using System.Security.Cryptography;
using System.Text;

namespace StockAnalyse.Api.Security;

public sealed class AppAuthSettings
{
    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "StockAI@2026";

    private readonly byte[] _usernameHash;
    private readonly byte[] _passwordHash;

    public AppAuthSettings(string? username, string? password)
    {
        Username = string.IsNullOrWhiteSpace(username)
            ? DefaultUsername
            : username.Trim();
        _usernameHash = Hash(Username);
        _passwordHash = Hash(string.IsNullOrEmpty(password) ? DefaultPassword : password);
    }

    public string Username { get; }

    public bool IsValid(string username, string password)
    {
        var usernameValid = CryptographicOperations.FixedTimeEquals(
            Hash(username), _usernameHash);
        var passwordValid = CryptographicOperations.FixedTimeEquals(
            Hash(password), _passwordHash);
        return usernameValid & passwordValid;
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
