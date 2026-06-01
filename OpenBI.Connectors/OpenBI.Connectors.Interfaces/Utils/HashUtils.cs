using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace OpenBI.Connectors.Interfaces.Utils
{
    public static class HashUtils
    {
        public static string ComputeSha256Hash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = sha256.ComputeHash(bytes);

                // Convert byte array to hex string
                StringBuilder hashString = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    hashString.Append(b.ToString("x2"));
                }
                return hashString.ToString();
            }
        }
    }
}
