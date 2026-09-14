namespace TravelExpense.Api.Auth;

public static class PasswordHasher
{
    public static string Hash(string plainText) => BCrypt.Net.BCrypt.HashPassword(plainText, workFactor: 11);

    public static bool Verify(string plainText, string hash) => BCrypt.Net.BCrypt.Verify(plainText, hash);
}
