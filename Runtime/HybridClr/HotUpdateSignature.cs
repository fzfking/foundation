using System;
using System.Security.Cryptography;

namespace Foundation.Infrastructure.HybridClr
{
    /// <summary>
    /// The signed form of a downloaded assembly (hot-update code, or AOT metadata, which carries IL the
    /// interpreter runs): the six bytes "FDSIG1", the signature length (Int32, little endian), the RSA
    /// signature of the assembly bytes (SHA-256, PKCS#1 v1.5), then those bytes. The build signs with a
    /// private key that never ships; the player verifies with the public key compiled into its AOT code.
    /// </summary>
    public static class HotUpdateSignature
    {
        private static readonly byte[] Magic = { (byte)'F', (byte)'D', (byte)'S', (byte)'I', (byte)'G', (byte)'1' };
        private const int HeaderLength = 6 + 4;
        private const int MaxSignatureLength = 1024;

        public static byte[] Wrap(byte[] payload, byte[] signature)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (signature == null || signature.Length == 0 || signature.Length > MaxSignatureLength)
                throw new ArgumentException("A signature is required.", nameof(signature));

            var signed = new byte[HeaderLength + signature.Length + payload.Length];
            Buffer.BlockCopy(Magic, 0, signed, 0, Magic.Length);
            WriteInt32(signed, Magic.Length, signature.Length);
            Buffer.BlockCopy(signature, 0, signed, HeaderLength, signature.Length);
            Buffer.BlockCopy(payload, 0, signed, HeaderLength + signature.Length, payload.Length);
            return signed;
        }

        public static bool TryUnwrap(byte[] signed, out byte[] payload, out byte[] signature)
        {
            payload = null;
            signature = null;
            if (signed == null || signed.Length < HeaderLength)
                return false;

            for (var index = 0; index < Magic.Length; index++)
                if (signed[index] != Magic[index])
                    return false;

            var signatureLength = ReadInt32(signed, Magic.Length);
            if (signatureLength <= 0 || signatureLength > MaxSignatureLength || HeaderLength + signatureLength > signed.Length)
                return false;

            signature = new byte[signatureLength];
            Buffer.BlockCopy(signed, HeaderLength, signature, 0, signatureLength);
            payload = new byte[signed.Length - HeaderLength - signatureLength];
            Buffer.BlockCopy(signed, HeaderLength + signatureLength, payload, 0, payload.Length);
            return true;
        }

        /// <summary>Signs assembly bytes with a private key (build side) and returns them in the signed form.</summary>
        public static byte[] Sign(byte[] payload, RSAParameters privateKey)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            using var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(privateKey);
            return Wrap(payload, rsa.SignHash(Hash(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        // Constructed directly rather than through SHA256.Create()/RSA.Create(), whose by-name lookup managed code stripping
        // can break in a player build.
        internal static byte[] Hash(byte[] payload)
        {
            using var sha = new SHA256Managed();
            return sha.ComputeHash(payload);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt32(byte[] buffer, int offset)
            => buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24;
    }

    /// <summary>Checks a downloaded assembly before anything of it is loaded.</summary>
    public interface IHotUpdateAssemblyVerifier
    {
        /// <summary>The assembly bytes of a signed blob whose signature checks out; throws <see cref="HotUpdateSignatureException"/> otherwise.</summary>
        byte[] Verify(string address, byte[] signedBytes);
    }

    public sealed class RsaHotUpdateAssemblyVerifier : IHotUpdateAssemblyVerifier
    {
        private readonly RSAParameters _publicKey;

        public RsaHotUpdateAssemblyVerifier(string modulusBase64, string exponentBase64)
        {
            if (string.IsNullOrWhiteSpace(modulusBase64))
                throw new ArgumentException("A modulus is required.", nameof(modulusBase64));
            if (string.IsNullOrWhiteSpace(exponentBase64))
                throw new ArgumentException("An exponent is required.", nameof(exponentBase64));
            _publicKey = new RSAParameters
            {
                Modulus = Convert.FromBase64String(modulusBase64),
                Exponent = Convert.FromBase64String(exponentBase64),
            };
        }

        public byte[] Verify(string address, byte[] signedBytes)
        {
            if (!HotUpdateSignature.TryUnwrap(signedBytes, out var payload, out var signature))
                throw new HotUpdateSignatureException($"'{address}' is not signed; it is not loaded.");

            using var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(_publicKey);
            if (!rsa.VerifyHash(HotUpdateSignature.Hash(payload), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new HotUpdateSignatureException($"The signature of '{address}' does not match its contents; it is not loaded.");

            return payload;
        }
    }

    public sealed class HotUpdateSignatureException : Exception
    {
        public HotUpdateSignatureException(string message) : base(message)
        {
        }
    }
}
