
using System.Security.Cryptography;
using System.Text;

namespace MyTts.Helpers
{
    public static class TextHasher
    {
        public static string ComputeMd5Hash(string rawData)
        {
            using (MD5 md5Hash = MD5.Create())
            {
                // Convert the input string to a byte array and compute the hash.
                byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

                // Create a new StringBuilder to collect the bytes and create a string.
                StringBuilder sBuilder = new StringBuilder();

                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }

                // Return the hexadecimal string.
                return sBuilder.ToString();
            }
        }
        public static bool HasTextChangedMd5(string currentText, string previousHash)
        {
            string currentHash = ComputeMd5Hash(currentText);
            return !string.Equals(currentHash, previousHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}