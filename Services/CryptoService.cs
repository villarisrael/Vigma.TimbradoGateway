using System.Security.Cryptography;
using System.Text;

namespace Vigma.TimbradoGateway.Services
{
    public class CryptoService
    {
        private readonly byte[] _key;

        public CryptoService(IConfiguration cfg)
        {
            // Debe ser 32 bytes (AES-256). Guardarlo como base64 en settings.
            var b64 = cfg["Crypto:KeyBase64"];
            if (string.IsNullOrWhiteSpace(b64))
                throw new InvalidOperationException("Falta Crypto:KeyBase64 en configuración.");

            _key = Convert.FromBase64String(b64);
            if (_key.Length != 32)
                throw new InvalidOperationException("Crypto:KeyBase64 debe decodificar a 32 bytes (AES-256).");
        }

        public string EncryptToBase64(string plain)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            var plainBytes = Encoding.UTF8.GetBytes(plain);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            // payload = IV + CIPHER
            var payload = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, payload, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, payload, aes.IV.Length, cipherBytes.Length);

            return Convert.ToBase64String(payload);
        }

        public string DecryptFromBase64(string b64)
        {
            var payload = Convert.FromBase64String(b64);

            using var aes = Aes.Create();
            aes.Key = _key;

            var iv = new byte[16];
            Buffer.BlockCopy(payload, 0, iv, 0, 16);

            var cipher = new byte[payload.Length - 16];
            Buffer.BlockCopy(payload, 16, cipher, 0, cipher.Length);

            using var decryptor = aes.CreateDecryptor(aes.Key, iv);
            var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
