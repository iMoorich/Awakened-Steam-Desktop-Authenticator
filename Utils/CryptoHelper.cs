using System;
using System.Security.Cryptography;
using System.Text;

namespace SteamGuard
{
    /// <summary>
    /// Утилиты для криптографических операций
    /// </summary>
    public static class CryptoHelper
    {
        /// <summary>
        /// Шифрует пароль с использованием RSA и возвращает Base64 строку
        /// </summary>
        /// <param name="password">Пароль для шифрования</param>
        /// <param name="modulusHex">Модуль RSA ключа в hex формате</param>
        /// <param name="exponentHex">Экспонента RSA ключа в hex формате</param>
        /// <returns>Зашифрованный пароль в Base64</returns>
        public static string EncryptPasswordRsa(string password, string modulusHex, string exponentHex)
        {
            try
            {
                var modBytes = HexToBytes(modulusHex);
                var expBytes = HexToBytes(exponentHex);

                var rsaParams = new RSAParameters
                {
                    Modulus = modBytes,
                    Exponent = expBytes
                };

                using var rsa = RSA.Create();
                rsa.ImportParameters(rsaParams);

                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var encrypted = rsa.Encrypt(passwordBytes, RSAEncryptionPadding.Pkcs1);

                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"RSA encryption error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Конвертирует hex строку в массив байтов
        /// </summary>
        /// <param name="hex">Hex строка</param>
        /// <returns>Массив байтов</returns>
        public static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                throw new ArgumentException("Hex string cannot be null or empty", nameof(hex));

            if (hex.Length % 2 != 0)
                throw new ArgumentException("Hex string must have even length", nameof(hex));

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// Конвертирует массив байтов в hex строку
        /// </summary>
        /// <param name="bytes">Массив байтов</param>
        /// <returns>Hex строка</returns>
        public static string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }
    }
}
