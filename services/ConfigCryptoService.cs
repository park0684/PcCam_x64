using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PcCam_x64.Services
{
    /// <summary>
    /// 설정 파일에 저장되는 민감 정보를 암호화/복호화하는 서비스.
    /// 
    /// 현재 사용 대상:
    /// - ONVIF Password
    /// 
    /// 저장 형식:
    /// - 평문: 1234
    /// - 암호문: ENC:base64...
    /// 
    /// ENC: 접두어를 사용하여 기존 평문 설정과 암호화된 설정을 구분한다.
    /// 기존 평문 설정은 로드 가능하며, 다음 저장 시 자동으로 암호화된다.
    /// </summary>
    public static class ConfigCryptoService
    {
        private const string Prefix = "ENC:";

        /*
         * 주의:
         * 이 값은 설정 파일 평문 노출을 막기 위한 로컬 설정 암호화 키이다.
         * 고도의 보안 저장소 목적은 아니며, 사용자가 INI 파일을 직접 열었을 때
         * 비밀번호가 그대로 보이지 않게 하는 목적이다.
         */
        private const string Secret = "POSCAM-PC-CAM-CONFIG-LOCAL-KEY-2026";

        /// <summary>
        /// 문자열을 암호화한다.
        /// 
        /// 빈 값은 그대로 빈 값으로 반환한다.
        /// 이미 ENC: 접두어가 있으면 중복 암호화하지 않는다.
        /// </summary>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return "";

            if (IsEncrypted(plainText))
                return plainText;

            byte[] key;
            byte[] iv;

            CreateKeyAndIv(out key, out iv);

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] encryptedBytes = encryptor.TransformFinalBlock(
                        plainBytes,
                        0,
                        plainBytes.Length);

                    return Prefix + Convert.ToBase64String(encryptedBytes);
                }
            }
        }

        /// <summary>
        /// 문자열을 복호화한다.
        /// 
        /// ENC: 접두어가 없으면 기존 평문 설정으로 판단하고 그대로 반환한다.
        /// 복호화 실패 시 프로그램 실행이 막히지 않도록 원문을 반환한다.
        /// </summary>
        public static string Decrypt(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (!IsEncrypted(value))
                return value;

            try
            {
                string base64 = value.Substring(Prefix.Length);

                byte[] encryptedBytes = Convert.FromBase64String(base64);

                byte[] key;
                byte[] iv;

                CreateKeyAndIv(out key, out iv);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    {
                        byte[] plainBytes = decryptor.TransformFinalBlock(
                            encryptedBytes,
                            0,
                            encryptedBytes.Length);

                        return Encoding.UTF8.GetString(plainBytes);
                    }
                }
            }
            catch
            {
                /*
                 * 설정 파일이 손상되었거나 이전 버전과 맞지 않는 경우에도
                 * 설정 화면에서 사용자가 다시 입력할 수 있도록 원문을 반환한다.
                 */
                return value;
            }
        }

        /// <summary>
        /// 값이 암호화된 설정값인지 확인한다.
        /// </summary>
        public static bool IsEncrypted(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.StartsWith(Prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// 고정 Secret에서 AES Key/IV를 생성한다.
        /// </summary>
        private static void CreateKeyAndIv(out byte[] key, out byte[] iv)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(Secret));

                key = hash;

                iv = new byte[16];
                Array.Copy(hash, 0, iv, 0, iv.Length);
            }
        }
    }
}