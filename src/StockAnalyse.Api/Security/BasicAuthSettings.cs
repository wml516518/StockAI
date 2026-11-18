using System.Security.Cryptography;
using System.Text;

namespace StockAnalyse.Api.Security;

public sealed class BasicAuthSettings
{
    private readonly byte[] _usernameHash;
    private readonly byte[] _passwordHash;

    public BasicAuthSettings(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "APP_AUTH_USERNAME and APP_AUTH_PASSWORD must be configured before StockAI starts.");
        }

        var normalizedUsername = username.Trim();
        if (normalizedUsername.Contains(':'))
        {
            throw new InvalidOperationException("APP_AUTH_USERNAME must not contain a colon.");
        }

        Username = normalizedUsername;
        _usernameHash = Hash(normalizedUsername);
        _passwordHash = Hash(password);
    }

    public string Username { get; }

    public bool IsValid(string username, string password)
    {
        var usernameValid = CryptographicOperations.FixedTimeEquals(Hash(username), _usernameHash);
        var passwordValid = CryptographicOperations.FixedTimeEquals(Hash(password), _passwordHash);
        return usernameValid & passwordValid;
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
