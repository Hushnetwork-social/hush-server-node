using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Olimpo;

public class EncryptKeys
{
    private string _publicKeyXml;
    private string _privateKeyXml;

    public string PublicKey { get; private set; }

    public string PrivateKey { get; private set; }

    public EncryptKeys()
    {
        using(var rsaKey = new RSACryptoServiceProvider())
        {
            this._publicKeyXml = rsaKey.ToXmlString(false);
            this._privateKeyXml = rsaKey.ToXmlString(true);

            this.PublicKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(this._publicKeyXml));
            this.PrivateKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(this._privateKeyXml));

            rsaKey.PersistKeyInCsp = false;
            rsaKey.Clear();
        } 

        // int keySize = 2048;
        // using (RSA rsa = RSA.Create(keySize)) // Use a more secure key size (e.g., 2048 or 4096)
        // {
        //     // Export Public Key
        //     byte[] publicKeyBytes = rsa.ExportRSAPublicKey(); // Export directly as byte array
        //     string publicKey = Convert.ToBase64String(publicKeyBytes); // Encode as Base64

        //     // Export Private Key
        //     byte[] privateKeyBytes = rsa.ExportRSAPrivateKey(); // Export directly as byte array
        //     string privateKey = Convert.ToBase64String(privateKeyBytes); // Encode as Base64

        //     this.PublicKey = publicKey;
        //     this.PrivateKey = privateKey;
        // }
    }

    // public static string Encrypt(string message, string publicKey)
    // {
    //     try
    //     {
    //         using (RSA rsa = RSA.Create())
    //         {
    //             // 1. Import RSA Public Key (Handle potential format issues)
    //             if (TryImportPublicKey(rsa, publicKey))
    //             {
    //                 // 2. Generate a random AES key and IV
    //                 using (Aes aes = Aes.Create())
    //                 {
    //                     aes.GenerateKey();
    //                     aes.GenerateIV();

    //                     // 3. Encrypt the message with the AES key and IV
    //                     byte[] encryptedData = EncryptAes(message, aes.Key, aes.IV);

    //                     // 4. Encrypt the AES key with the RSA public key
    //                     byte[] encryptedAesKey = rsa.Encrypt(aes.Key, RSAEncryptionPadding.OaepSHA256);

    //                     // 5. Combine the encrypted data, key, and IV into a single byte array
    //                     byte[] result = Combine(encryptedAesKey, aes.IV, encryptedData);

    //                     // 6. Encode the result as Base64
    //                     return Convert.ToBase64String(result);
    //                 }
    //             }
    //             else
    //             {
    //                 throw new ArgumentException("Invalid RSA public key format.");
    //             }
    //         }
    //     }
    //     catch (CryptographicException ex)
    //     {
    //         throw new CryptographicException("Encryption failed due to a cryptographic error.", ex);
    //     }
    // }

    // private static bool TryImportPublicKey(RSA rsa, string publicKey)
    // {
    //     try
    //     {
    //         byte[] publicKeyBytes = Convert.FromBase64String(publicKey);
    //         rsa.ImportRSAPublicKey(publicKeyBytes, out _);
    //         return true;
    //     }
    //     catch (CryptographicException)
    //     {
    //         try
    //         {
    //             // Attempt to import as X.509 SubjectPublicKeyInfo
    //             byte[] publicKeyBytes = Convert.FromBase64String(publicKey);
    //             rsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
    //             return true;
    //         }
    //         catch (CryptographicException)
    //         {
    //             return false; // Neither format worked
    //         }
    //     }
    // }

    // private static byte[] EncryptAes(string message, byte[] key, byte[] iv)
    // {
    //     // Declare msEncrypt outside of the using block
    //     MemoryStream msEncrypt = new MemoryStream();

    //     using (Aes aes = Aes.Create())
    //     using (ICryptoTransform encryptor = aes.CreateEncryptor(key, iv))
    //     using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
    //     using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
    //     {
    //         swEncrypt.Write(message); 

    //     }

    //     return msEncrypt.ToArray(); // msEncrypt is accessible here
    // }

    // private static byte[] Combine(params byte[][] arrays)
    // {
    //     byte[] rv = new byte[arrays.Sum(a => a.Length)];
    //     int offset = 0;
    //     foreach (byte[] array in arrays)

    //     {
    //         Buffer.BlockCopy(array, 0, rv, offset, array.Length);
    //         offset += array.Length;
    //     }
    //     return rv; 

    // }

    // public static string Decrypt(string encryptedMessage, string privateKey)
    // {
    //     using (var rsa = new RSACryptoServiceProvider())
    //     {
    //         var privateKeyBytes = Convert.FromBase64String(privateKey);
    //         rsa.ImportRSAPrivateKey(privateKeyBytes, out _);

    //         // 1. Decode the Base64-encoded encrypted data
    //         byte[] encryptedData = Convert.FromBase64String(encryptedMessage);

    //         // 2. Split the encrypted data into the encrypted AES key, IV, and encrypted message
    //         byte[] encryptedAesKey = new byte[rsa.KeySize / 8];  
    //         byte[] iv = new byte[16]; // AES IV is 16 bytes (128 bits)
    //         byte[] encryptedMessageData = new byte[encryptedData.Length - encryptedAesKey.Length - iv.Length];

    //         Buffer.BlockCopy(encryptedData, 0, encryptedAesKey, 0, encryptedAesKey.Length);
    //         Buffer.BlockCopy(encryptedData, encryptedAesKey.Length, iv, 0, iv.Length);
    //         Buffer.BlockCopy(encryptedData, encryptedAesKey.Length + iv.Length, encryptedMessageData, 0, encryptedMessageData.Length);

    //         // 3. Decrypt the AES key using the RSA private key
    //         byte[] aesKey = rsa.Decrypt(encryptedAesKey, RSAEncryptionPadding.OaepSHA256);

    //         // 4. Decrypt the message using the AES key and IV
    //         return DecryptAes(encryptedMessageData, aesKey, iv);
    //     }
    // }

    // private static string DecryptAes(byte[] encryptedMessageData, byte[] key, byte[] iv)
    // {
    //     using (var aes = Aes.Create())
    //     using (var decryptor = aes.CreateDecryptor(key, iv))
    //     using (var msDecrypt = new MemoryStream(encryptedMessageData))
    //     using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
    //     using (var srDecrypt = new StreamReader(csDecrypt))

    //     {
    //         return srDecrypt.ReadToEnd();
    //     }
    // }

    public static string Encrypt(string message, string publicKey)
    {
        using (var rsa = new RSACryptoServiceProvider())
        {
            var publicKeyBytes = Convert.FromBase64String(publicKey);
            var publicKeyXml = Encoding.UTF8.GetString(publicKeyBytes);

            rsa.FromXmlString(publicKeyXml);
            byte[] dataToEncrypt = Encoding.UTF8.GetBytes(message);
            return Convert.ToBase64String(rsa.Encrypt(dataToEncrypt, true));
        }
    }

    public static string Decrypt(string encryptedMessage, string privateKey)
    {
        using (var rsa = new RSACryptoServiceProvider())
        {
            var encryptedMessageBytes = Convert.FromBase64String(encryptedMessage);

            var privateKeyBytes = Convert.FromBase64String(privateKey);
            var privateKeyXml = Encoding.UTF8.GetString(privateKeyBytes);

            rsa.FromXmlString(privateKeyXml);
            byte[] decryptedData = rsa.Decrypt(encryptedMessageBytes, true);
            return Encoding.UTF8.GetString(decryptedData);
        }
    }
}
