namespace GameHub.Server.Auth;

using System.Security.Cryptography;

// Хеширование паролей алгоритмом PBKDF2 (HMAC-SHA256) — встроен в .NET, пакеты не нужны.
// Для каждого пароля генерируется своя случайная соль, поэтому одинаковые пароли
// дают разные хеши, а заранее посчитанные таблицы (rainbow tables) бесполезны.
// Формат строки в БД:  PBKDF2-SHA256$600000$<соль base64>$<хеш base64>
public class PasswordHasher
{
    private const string Prefix = "PBKDF2-SHA256";
    private const int Iterations = 600_000; // рекомендация OWASP для PBKDF2-HMAC-SHA256
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Сравнение за постоянное время, чтобы по времени ответа нельзя было подбирать хеш
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // Если пользователя с таким email нет, всё равно считаем хеш: иначе по быстрому
    // ответу можно было бы узнать, какие email зарегистрированы
    private readonly Lazy<string> _dummyHash = new(() => new PasswordHasher().Hash("dummy-password"));

    public void VerifyDummy(string password) => Verify(password, _dummyHash.Value);
}
