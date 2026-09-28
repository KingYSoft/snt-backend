using System;
using System.Security.Cryptography;
using Abp.Dependency;

namespace SntBackend.Application.User
{
    public class PasswordHashService : ITransientDependency
    {
        private const int DefaultIterations = 200000;
        private const int SaltSize = 16;
        private const int HashSize = 20;

        public bool Verify(string password, byte[] expectedHash, byte[] salt, int iterations)
        {
            if (string.IsNullOrEmpty(password) || expectedHash == null || expectedHash.Length == 0
                || salt == null || salt.Length == 0 || iterations <= 0)
                return false;

            using var derive = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA1);
            var actualHash = derive.GetBytes(expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        public PasswordHashResult Create(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
                throw new ArgumentException("Password must be at least 6 characters.", nameof(password));

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            using var derive = new Rfc2898DeriveBytes(password, salt, DefaultIterations, HashAlgorithmName.SHA1);
            return new PasswordHashResult(derive.GetBytes(HashSize), salt, DefaultIterations);
        }
    }

    public sealed class PasswordHashResult
    {
        public PasswordHashResult(byte[] hash, byte[] salt, int iterations)
        {
            Hash = hash;
            Salt = salt;
            Iterations = iterations;
        }

        public byte[] Hash { get; }
        public byte[] Salt { get; }
        public int Iterations { get; }
    }
}
