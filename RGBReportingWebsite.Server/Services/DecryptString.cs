using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RGBReportingWebsite.Server.Services
{
    internal class DecryptString
    {
        public static string Run(string stringToDecrypt)
        {
            string key = "frg%tf$eqasJu7Ij"; // 128-bit key (16 characters)
            string iv = "gtTf67f45ws3d2Dl";
            string plainText = string.Empty;


            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.IV = Encoding.UTF8.GetBytes(iv); // Initialization vector (IV)
                                                     // Create a decryptor to perform the stream transform
                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
                // Create the streams used for decryption
                using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(stringToDecrypt)))
                {
                    using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader sr = new StreamReader(cs))
                        {
                            plainText = sr.ReadToEnd();
                        }
                    }
                }
            }

            return plainText;
        }
    }
}
