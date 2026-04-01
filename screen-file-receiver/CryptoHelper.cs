using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace screen_file_receiver
{
    public static class CryptoHelper
    {
        public static void EncryptStream(Stream input, Stream output, string password)
        {
            byte[] key = DeriveKey(password);
            using (DESCryptoServiceProvider des = new DESCryptoServiceProvider())
            {
                des.Key = key;
                des.IV = key;
                var cryptoStream = new CryptoStream(output, des.CreateEncryptor(), CryptoStreamMode.Write);
                input.CopyTo(cryptoStream);
                cryptoStream.FlushFinalBlock();
                // 不 Dispose CryptoStream，避免关闭 output
            }
        }

        public static void DecryptStream(Stream input, Stream output, string password)
        {
            byte[] key = DeriveKey(password);
            using (DESCryptoServiceProvider des = new DESCryptoServiceProvider())
            {
                des.Key = key;
                des.IV = key;
                var cryptoStream = new CryptoStream(input, des.CreateDecryptor(), CryptoStreamMode.Read);
                cryptoStream.CopyTo(output);
                // 不 Dispose CryptoStream，避免关闭 input
            }
        }

        private static byte[] DeriveKey(string password)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(password);
            byte[] key = new byte[8];
            Array.Copy(bytes, key, Math.Min(bytes.Length, key.Length));
            return key;
        }
    }
}
